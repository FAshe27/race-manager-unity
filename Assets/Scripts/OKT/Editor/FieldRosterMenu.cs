// Finds the hand-set field asset, making it (off, with a made-up 12-kart field) if it isn't there yet.
// The menu item OKT > Field Roster lives on FieldRosterWindow (FieldRosterTable.cs).
using UnityEditor;
using UnityEngine;

namespace OKT.EditorTools
{
    public static class FieldRosterMenu
    {
        public static FieldRoster LoadOrMake()
        {
            var r = AssetDatabase.LoadAssetAtPath<FieldRoster>(FieldRoster.AssetPath);
            if (r == null)
            {
                r = ScriptableObject.CreateInstance<FieldRoster>();
                foreach (var d in FieldGen.Make(12, 1, 11.03f))
                    r.drivers.Add(new RosterEntry { name = d.name, number = d.number, color = d.color });
                AssetDatabase.CreateAsset(r, FieldRoster.AssetPath); AssetDatabase.SaveAssets();
            }
            return r;
        }
    }
}
