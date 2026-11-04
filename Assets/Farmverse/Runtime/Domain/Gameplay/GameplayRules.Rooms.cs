#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Farmverse.Domain.Gameplay
{
    /// <summary>
    /// The farmhouse's rooms (plan: Farmhouse interiors): five dioramas that open as the farmhouse levels
    /// up, each with extra furniture the player can buy for coins. The room prefabs carry every item as a
    /// hidden child named opt_&lt;id&gt;; owning the item shows it.
    /// </summary>
    public static partial class GameplayRules
    {
        public static readonly string[] RoomNames = { "Living room", "Kitchen", "Bedroom", "Bathroom", "Study" };

        /// <summary>Rooms open at a farmhouse level: living room and kitchen from the start, then one more a level.</summary>
        public static int RoomsOpen(int farmhouseLevel) => Math.Clamp(1 + Math.Max(1, farmhouseLevel), 2, RoomNames.Length);

        /// <summary>The farmhouse level a room needs (0 = living room).</summary>
        public static int RoomLevel(int room) => Math.Max(1, room);

        public sealed class RoomItem
        {
            public RoomItem(string id, int room, string name, int price) { Id = id; Room = room; Name = name; Price = price; }
            public string Id { get; }
            public int Room { get; }
            public string Name { get; }
            /// <summary>Coins.</summary>
            public int Price { get; }
        }

        public static readonly IReadOnlyList<RoomItem> RoomItems = new[]
        {
            // living room
            new RoomItem("tree_indoor", 0, "Christmas tree", 2400),
            new RoomItem("pouf", 0, "Pouf", 350),
            new RoomItem("armchair_2", 0, "Second armchair", 900),
            new RoomItem("guitar", 0, "Guitar", 650),
            new RoomItem("picture_sunset", 0, "Sunset picture", 500),
            new RoomItem("plant_tall", 0, "Tall plant", 420),
            new RoomItem("books", 0, "Book pile", 180),
            // kitchen
            new RoomItem("microwave", 1, "Microwave", 700),
            new RoomItem("mixer", 1, "Stand mixer", 550),
            new RoomItem("island", 1, "Kitchen island", 1600),
            new RoomItem("cutting_board", 1, "Cutting board", 150),
            new RoomItem("herb_pot", 1, "Herb pot", 260),
            new RoomItem("picture_fruit", 1, "Fruit picture", 480),
            // bedroom
            new RoomItem("teddy", 2, "Teddy bear", 300),
            new RoomItem("toy_car", 2, "Toy car", 240),
            new RoomItem("closet_2", 2, "Wardrobe", 1300),
            new RoomItem("bed_clock", 2, "Alarm clock", 160),
            new RoomItem("picture_flowers", 2, "Flower picture", 480),
            new RoomItem("plant_bedroom", 2, "Potted plant", 320),
            // bathroom
            new RoomItem("bath_mat", 3, "Bath mat", 220),
            new RoomItem("bath_plant", 3, "Fern", 320),
            new RoomItem("bath_picture", 3, "Seaside picture", 480),
            new RoomItem("toothbrushes", 3, "Toothbrushes", 90),
            // study
            new RoomItem("console", 4, "Games console", 1800),
            new RoomItem("exercise_bike", 4, "Exercise bike", 2200),
            new RoomItem("study_guitar", 4, "Guitar", 650),
            new RoomItem("study_plant", 4, "Plant", 420),
            new RoomItem("study_picture", 4, "Landscape picture", 500),
            new RoomItem("study_clock", 4, "Wall clock", 260),
        };

        public static RoomItem? RoomItemById(string id) => RoomItems.FirstOrDefault(i => i.Id == id);
        public static IEnumerable<RoomItem> RoomItemsIn(int room) => RoomItems.Where(i => i.Room == room);
    }
}
