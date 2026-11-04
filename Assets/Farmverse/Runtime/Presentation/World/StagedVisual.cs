#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Farmverse.Presentation.World
{
    /// <summary>
    /// Shows the child that matches a visual's stage. Models carry their stages as
    /// children named after <see cref="VisualStage"/> ("Seeded", "Sprout", "Young",
    /// "Grown", "Ready", "Working", "Idle"); a stage without its own child falls back
    /// to the nearest earlier one, so a model needs only the stages it looks
    /// different in. Children named "Product" show while something waits to be
    /// collected (eggs in the pen, a sack at the mill). A stage change pops the new
    /// look in. Prefabs made by the setup tool are
    /// a root (components, collider) holding the imported model as "Model".
    /// </summary>
    public sealed class StagedVisual : MonoBehaviour, IVisualStateReceiver
    {
        private static readonly VisualStage[] Order =
        {
            VisualStage.None, VisualStage.Idle, VisualStage.Seeded, VisualStage.Sprout, VisualStage.Young, VisualStage.Grown, VisualStage.Ready, VisualStage.Working,
        };

        private readonly Dictionary<VisualStage, GameObject> _stages = new Dictionary<VisualStage, GameObject>();
        private GameObject? _product;
        private VisualStage _shown = (VisualStage)(-1);
        private float _pop;
        private bool _reduceMotion;

        public VisualStage Stage => _shown;

        private Transform _model = null!;

        public static Transform ModelOf(Transform root) => root.Find("Model") ?? root;

        private void Awake()
        {
            _model = ModelOf(transform);
            foreach (Transform child in _model)
            {
                if (Enum.TryParse<VisualStage>(child.name, out var stage)) _stages[stage] = child.gameObject;
                else if (child.name == "Product") _product = child.gameObject;
                // pieces of stages the game no longer has (a broken machine, a dry field) never show
                else if (child.name == "Broken" || child.name == "Thirsty") child.gameObject.SetActive(false);
            }
        }

        public void OnVisualState(WorldVisual visual, bool reduceMotion)
        {
            _reduceMotion = reduceMotion;
            var stage = Pick(visual.Stage);
            if (stage != _shown)
            {
                foreach (var pair in _stages) pair.Value.SetActive(pair.Key == stage);
                if (_shown != (VisualStage)(-1) && !reduceMotion) _pop = 1f;
                _shown = stage;
            }
            if (_product != null) _product.SetActive((visual.Flags & VisualFlags.Collectable) != 0);
        }

        private VisualStage Pick(VisualStage wanted)
        {
            if (_stages.Count == 0 || _stages.ContainsKey(wanted)) return wanted;
            // nearest earlier stage the model has; growth runs Seeded → Ready
            // a stage before every child the model has shows the base alone
            var index = Array.IndexOf(Order, wanted);
            for (var i = index; i >= 0; i--)
                if (_stages.ContainsKey(Order[i])) return Order[i];
            return wanted;
        }

        private void Update()
        {
            if (_pop <= 0f) return;
            _pop = Mathf.Max(0f, _pop - Time.deltaTime * 3.5f);
            // a soft overshoot: grows a little past its size, settles back
            var s = 1f + Mathf.Sin((1f - _pop) * Mathf.PI) * 0.18f * _pop;
            _model.localScale = _reduceMotion ? Vector3.one : new Vector3(s, s, s);
        }
    }
}
