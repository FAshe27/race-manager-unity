// Menu OKT > Field Roster: selects the hand-set field asset (makes it, off, if it isn't there yet).
using UnityEditor;
using UnityEngine;

namespace OKT.EditorTools
{
    public static class FieldRosterMenu
    {
        [MenuItem("OKT/Field Roster")]
        public static void Select()
        {
            var r = AssetDatabase.LoadAssetAtPath<FieldRoster>(FieldRoster.AssetPath);
            if (r == null)
            {
                r = ScriptableObject.CreateInstance<FieldRoster>();
                foreach (var d in FieldGen.Make(12, 1, 11.03f))
                    r.drivers.Add(new RosterEntry { name = d.name, number = d.number, color = d.color });
                AssetDatabase.CreateAsset(r, FieldRoster.AssetPath); AssetDatabase.SaveAssets();
            }
            Selection.activeObject = r; EditorGUIUtility.PingObject(r);
        }
    }
}
