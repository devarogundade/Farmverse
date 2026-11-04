using Farmverse.Presentation.World.Island;
using UnityEditor;

namespace Farmverse.Editor
{
    /// <summary>Farmverse ▸ Weather Preview: see each season and weather effect in Play mode without waiting for it.</summary>
    public static class WeatherPreview
    {
        private const string Menu = "Farmverse/Weather Preview/";

        [MenuItem(Menu + "Follow the island", priority = 0)] public static void Off() { IslandWeatherFx.Preview = null; IslandFestival.Preview = false; }
        [MenuItem(Menu + "Winter Festival", priority = 20)] public static void Festival() { IslandWeatherFx.Preview = "snow"; IslandFestival.Preview = true; }
        [MenuItem(Menu + "Rain", priority = 11)] public static void Rain() => IslandWeatherFx.Preview = "rain";
        [MenuItem(Menu + "Storm", priority = 12)] public static void Storm() => IslandWeatherFx.Preview = "storm";
        [MenuItem(Menu + "Snow", priority = 13)] public static void Snow() => IslandWeatherFx.Preview = "snow";
        [MenuItem(Menu + "Blizzard", priority = 14)] public static void Blizzard() => IslandWeatherFx.Preview = "blizzard";
        [MenuItem(Menu + "Autumn leaves", priority = 15)] public static void Leaves() => IslandWeatherFx.Preview = "leaves";
        [MenuItem(Menu + "Spring petals", priority = 16)] public static void Petals() => IslandWeatherFx.Preview = "petals";
        [MenuItem(Menu + "Drought", priority = 17)] public static void Drought() => IslandWeatherFx.Preview = "drought";
    }
}
