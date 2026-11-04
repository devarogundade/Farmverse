#nullable enable
using UnityEditor;

namespace Farmverse.Editor
{
    /// <summary>
    /// The island's economy lives in plain C# objects that can't survive a script reload in the
    /// middle of play. So scripts changed while playing are compiled only once play stops (Unity's
    /// "Recompile After Finished Playing"), set here for everyone who opens the project.
    /// </summary>
    [InitializeOnLoad]
    public static class PlayModeSafety
    {
        private const string Key = "ScriptCompilationDuringPlay";
        private const int RecompileAfterFinishedPlaying = 1;

        static PlayModeSafety()
        {
            if (EditorPrefs.GetInt(Key, 0) != RecompileAfterFinishedPlaying) EditorPrefs.SetInt(Key, RecompileAfterFinishedPlaying);
        }
    }
}
