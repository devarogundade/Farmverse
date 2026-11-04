#nullable enable
using System.Threading;
using Farmverse.Application.Config;
using Farmverse.Application.Features;
using Farmverse.Application.Remote;
using Farmverse.Application.Session;
using Farmverse.Core.Logging;
using Farmverse.Core.Reactive;
using Farmverse.Core.Scheduling;
using Farmverse.Core.Time;
using Farmverse.Infrastructure.Firebase;
using Farmverse.Infrastructure.Unity;
using Farmverse.Presentation.UI.Game;
using Farmverse.Presentation.World.Island;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Farmverse.Bootstrap
{
    /// <summary>
    /// The composition root (02 §3): the only place that knows every concrete type. Lives in the boot
    /// scene, builds the server-side services once (Firebase, sign-in, Remote Config, push, analytics),
    /// opens the Island scene (the game and its own UI, playable offline) and starts the session.
    /// Server features attach when the session is Ready.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class GameRoot : MonoBehaviour
    {
        [SerializeField] private EnvironmentConfig? environment;
        [SerializeField, Tooltip("The game scene (must be in the build settings).")] private string islandScene = "Island";

        private readonly CancellationTokenSource _life = new CancellationTokenSource();
        private readonly CompositeDisposable _scope = new CompositeDisposable();
        private ILog _log = NullLog.Instance;
        private SessionService _session = null!;
        private NotificationService _notifications = null!;
        private FeatureFlags _flags = null!;
        private ServerCalls _server = null!;
        private FriendsService _friends = null!;
        private RewardsService _rewards = null!;
        private NewsService _news = null!;
        private CatalogueService _catalogue = null!;
        private WalletService _wallet = null!;
        private ShopService _shop = null!;
        private CouponsService _coupons = null!;
        private ProfileService _profile = null!;
        private LeaderboardService _board = null!;
        private InboxService _inbox = null!;
        private AdsService _ads = null!;
        private AccountService _account = null!;
        private PurchaseService _purchases = null!;
        private GameAnalytics? _gameAnalytics;
        private bool _composed;

        private void Awake() => DontDestroyOnLoad(gameObject);

        /// <summary>
        /// Start the game over (after switching to another account): this root goes, the boot scene makes a
        /// new one, which signs in as the account now current and loads its farm.
        /// </summary>
        public void Restart()
        {
            Destroy(gameObject);
            SceneManager.LoadScene(0, LoadSceneMode.Single);
        }

        private void Start()
        {
            if (environment == null)
            {
                Debug.LogError("GameRoot is missing its Environment (Content/Config/Environments/Environment.dev).", this);
                enabled = false;
                return;
            }
            Compose(environment);
            _composed = true;
            OpenIsland();
            _ = _session.Start(_life.Token);
            // invite links (https://farmverse.world/invite/CODE): accepted now, or once signed in
            UnityEngine.Application.deepLinkActivated += OnDeepLink;
            if (!string.IsNullOrEmpty(UnityEngine.Application.absoluteURL)) OnDeepLink(UnityEngine.Application.absoluteURL);
        }

        private void OnDeepLink(string url) => _friends.OfferLink(url);

        /// <summary>The game itself: the Island scene replaces the boot scene (this object stays).</summary>
        private void OpenIsland()
        {
            if (!UnityEngine.Application.CanStreamedLevelBeLoaded(islandScene))
            {
                Debug.LogError($"GameRoot: the {islandScene} scene is not in the build settings.", this);
                return;
            }
            SceneManager.LoadSceneAsync(islandScene, LoadSceneMode.Single);
        }

        private void OnApplicationPause(bool paused)
        {
            if (!_composed) return;
            if (paused) _session.Pause();
            else _session.Resume();
        }

        private void OnDestroy()
        {
            _life.Cancel();
            UnityEngine.Application.deepLinkActivated -= OnDeepLink;
            _scope.Dispose();
            _gameAnalytics?.Dispose();
            IslandShadow.Attach(null);
            if (!_composed) return;
            _flags?.Dispose();
            _notifications?.Dispose();
            if (GameUi.Friends == _friends) GameUi.Friends = null;
            if (GameUi.Notifications == _notifications) GameUi.Notifications = null;
            if (GameUi.Rewards == _rewards) GameUi.Rewards = null;
            if (GameUi.News == _news) GameUi.News = null;
            if (Wallet.Service == _wallet) Wallet.Service = null;
            if (Wallet.Catalogue == _catalogue) Wallet.Catalogue = null;
            if (Wallet.Shop == _shop) Wallet.Shop = null;
            if (GameUi.Coupons == _coupons) GameUi.Coupons = null;
            if (GameUi.Profile == _profile) GameUi.Profile = null;
            if (GameUi.Board == _board) GameUi.Board = null;
            if (GameUi.Inbox == _inbox) GameUi.Inbox = null;
            if (GameUi.Ads == _ads) GameUi.Ads = null;
            if (GameUi.Account == _account) GameUi.Account = null;
            GameUi.RetrySession = null;
            GameUi.SystemNotice = null;
            if (GameUi.Purchases == _purchases) GameUi.Purchases = null;
            _purchases?.Dispose();
            if (GameUi.Visits == _server) GameUi.Visits = null;
            _friends?.Dispose();
            _rewards?.Dispose();
            _news?.Dispose();
            _wallet?.Dispose();
            _shop?.Dispose();
            _coupons?.Dispose();
            _profile?.Dispose();
            _board?.Dispose();
            _inbox?.Dispose();
            _catalogue?.Dispose();
        }

        private void Compose(EnvironmentConfig config)
        {
            var firebase = config.ToFirebase();
            var crash = new CrashlyticsReporter();
            _log = new Farmverse.Core.Logging.Logger(new ILogSink[] { new UnityLogSink() }, config.MinimumLog);

            // engine services
            var main = gameObject.AddComponent<UnityMainThread>();
            var connectivity = gameObject.AddComponent<UnityConnectivity>();
            var local = new FileLocalStore(_log);
            var app = new UnityAppInfo();
            var scheduler = TaskDelayScheduler.Instance;
            var clock = new ServerClock(new StopwatchTimer(), SystemClock.Instance);

            // Firebase-backed ports, bound after the platform step
            var documents = new DeferredDocumentStore();
            var auth = new DeferredAuth();
            var remoteConfig = new DeferredRemoteConfig();
            var push = new FcmPushService(main);
            // a tapped notification opens its screen (the inbox when the route isn't one)
            push.Opened += (route, _) => GameUi.PendingRoute = route;
            var platform = new FirebaseComposition(firebase, main, _log, documents, auth, remoteConfig, afterBoot: () =>
            {
                crash.Enable();
                push.Start();
            });
            var analytics = new LogAnalytics(crash, _log);

            // application
            var callables = new CallableClient(new FunctionsCallableTransport(firebase), clock, _log);
            System.Func<Farmverse.Contracts.RequestMeta> meta = () => _session.Meta();
            _session = new SessionService(new SessionDependencies
            {
                Platform = platform,
                RemoteConfig = remoteConfig,
                Auth = auth,
                Documents = documents,
                Connectivity = connectivity,
                App = app,
                Crash = crash,
                Callables = callables,
                Scheduler = scheduler,
                Log = _log,
            });
            _notifications = new NotificationService(documents, callables, push, clock, meta, new FirebaseInstallationId(local, _log), local);
            _flags = new FeatureFlags(remoteConfig);
            _server = new ServerCalls(callables, meta);
            _friends = new FriendsService(documents, _server);
            GameUi.Notifications = _notifications;   // the push switch in Settings
            GameUi.Friends = _friends;   // the game's Friends screen (Island scene) reaches the server through this
            _rewards = new RewardsService(documents, _server);
            GameUi.Rewards = _rewards;
            _news = new NewsService(documents);
            GameUi.News = _news;
            _catalogue = new CatalogueService(documents, local);
            Wallet.Catalogue = _catalogue;   // prices the shop shows = prices the server charges
            _wallet = new WalletService(documents, _server, local, _catalogue);
            Wallet.Service = _wallet;       // gems: server-owned
            _shop = new ShopService(documents, _server, local);
            _coupons = new CouponsService(documents, _server, local);
            GameUi.Coupons = _coupons;      // coupon codes: server-issued from the type's pool
            _profile = new ProfileService(documents, _server);
            GameUi.Profile = _profile;      // name and avatar as friends and the board see them
            _board = new LeaderboardService(documents, () => clock.NowMs);
            GameUi.Board = _board;          // the weekly board, written by the server from each island run
            GameUi.Visits = _server;        // visits-get / visits-like
            _inbox = new InboxService(documents, _server, () => clock.NowMs);
            GameUi.Inbox = _inbox;          // mail and notifications, with rewards to collect
            _purchases = new PurchaseService(new UnityAppStore(), _server, _catalogue, documents);
            GameUi.Purchases = _purchases;  // real money: the store sells, iap-verify checks and pays
            _ads = new AdsService(new AdMobRewardedAds(config.RewardedAdUnitId), _server, () => _purchases.Unlocked.NoAds);
            _account = new AccountService(auth, _server, Restart, () => { PlayerPrefs.DeleteAll(); PlayerPrefs.Save(); });
            GameUi.Account = _account;
            GameUi.RetrySession = () => _ = _session.Start(_life.Token);      // Save Farm (Google / Apple), switching farms, deleting the account
            GameUi.Ads = _ads;              // rewarded ads: AdMob shows, ads-claim pays (verified by AdMob's callback when live)
            Wallet.Shop = _shop;            // boosts, free gift, starter pack, daily chests: server-owned
            // one raid a day, presence for the server's pushes, the island's own reminders on the phone
            var localNotes = new UnityLocalNotifications();
            GameUi.LocalNotifications = localNotes;
            var pinger = GetComponent<SessionPinger>() ?? gameObject.AddComponent<SessionPinger>();
            pinger.Init(_server, () => _session.State.Value == SessionState.Ready && _session.Uid != null, localNotes, _shop);
            var launchedAt = Time.realtimeSinceStartup;
            _gameAnalytics = new GameAnalytics(analytics, _session, () => (long)((Time.realtimeSinceStartup - launchedAt) * 1000));

            _scope.Add(_session.State.Subscribe(state =>
            {
                if (state == SessionState.Ready && _session.Uid != null)
                {
                    GameUi.SystemNotice = null;
                    _flags.Refresh(); // the session just fetched Remote Config
                    _notifications.Attach(_session.Uid);
                    _ = _notifications.RefreshRegistration(_life.Token);
                    _friends.Attach(_session.Uid);
                    _rewards.Attach(_session.Uid);
                    _news.Attach();
                    _catalogue.Attach();
                    _wallet.Attach(_session.Uid);
                    _shop.Attach(_session.Uid);
                    _coupons.Attach(_session.Uid);
                    _profile.Attach(_session.Uid);
                    _board.Attach(_session.Uid, GameUi.LanguageIso);
                    _inbox.Attach(_session.Uid);
                    _ads.Attach(_session.Uid);
                    _purchases.Attach(_session.Uid);
                    // cloud save (and rollout phase 1: mirror the island to the server engine and compare the runs)
                    var uid = _session.Uid;
                    IslandShadow.Attach(_server, uid, async () =>
                    {
                        var got = await documents.Get(Farmverse.Application.Documents.Paths.Island(uid), Farmverse.Core.Lists.Freshness.Server, _life.Token);
                        if (got.IsFailure || !got.Value.Exists || got.Value.Data == null) return null;
                        return new IslandShadow.CloudCopy { DeviceSave = got.Value.Data.TryGetValue("deviceSave", out var d) ? d as string : null };
                    });
                }
                else if (state is SessionState.Maintenance or SessionState.UpdateRequired or SessionState.Offline or SessionState.Failed)
                {
                    GameUi.SystemNotice = state switch
                    {
                        SessionState.Maintenance => "maintenance",
                        SessionState.UpdateRequired => "update",
                        _ => "noconnection",
                    };
                    if (_session.Error is { } stopped) Debug.LogWarning($"[GameRoot] session stopped in {state}: {stopped}");
                }
                else if (_session.Error is { } error)
                {
                    // the game plays on offline; screens for these come with the GameUi boot states
                    Debug.LogWarning($"[GameRoot] session stopped in {state}: {error}");
                }
            }));
        }
    }
}
