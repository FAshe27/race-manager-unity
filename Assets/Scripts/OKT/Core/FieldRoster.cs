// A hand-set field (Franklin 11-21: "the ability to manually set a field with names, numbers, and colors", and
// "a grid-order customization, so you can start certain drivers in certain positions").
// Edit Assets/Data/FieldRoster.asset in the Inspector (menu OKT > Field Roster selects it) and tick "Use Roster".
// Each line is one kart; the list's length is the field size. Everything else about each kart -- his pace within
// his tier, consistency, tires, gearing, balance -- is still drawn for the night as usual.
using System.Collections.Generic;
using UnityEngine;

namespace OKT
{
    public enum RosterTier { Auto, Fast, Mid, New }
    public enum RosterGrid { Qualifying, AsListed }

    [System.Serializable]
    public class RosterEntry
    {
        public string name = "Driver";
        [Range(0, 99)] public int number = 1;
        public Color color = Color.red;
        [Tooltip("Auto = the usual 20/70/10 fast/mid/new split by place in the list")]
        public RosterTier tier = RosterTier.Auto;
        [Tooltip("Starting spot (1 = pole). 0 = wherever the grid order puts him")]
        [Range(0, 40)] public int startPosition = 0;
    }

    [CreateAssetMenu(fileName = "FieldRoster", menuName = "OKT/Field Roster")]
    public class FieldRoster : ScriptableObject
    {
        [Tooltip("Off = a made-up field each race, as before")]
        public bool useRoster = false;
        [Tooltip("Qualifying = fastest qualifier on pole (as before). As Listed = the order of this list. " +
                 "Either way, anyone with a Start Position is put there first.")]
        public RosterGrid gridOrder = RosterGrid.Qualifying;
        public List<RosterEntry> drivers = new List<RosterEntry>();

        public const string AssetPath = "Assets/Data/FieldRoster.asset";
        public bool Active => useRoster && drivers != null && drivers.Count >= 2;
    }
}
