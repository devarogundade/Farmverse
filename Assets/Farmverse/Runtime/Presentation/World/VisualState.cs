#nullable enable
namespace Farmverse.Presentation.World
{
    /// <summary>A part of a world model that changes with what it shows (a crop's stage, a machine at work).</summary>
    public interface IVisualStateReceiver
    {
        void OnVisualState(WorldVisual visual, bool reduceMotion);
    }
}
