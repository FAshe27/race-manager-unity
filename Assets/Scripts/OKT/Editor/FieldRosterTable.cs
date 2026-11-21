// The hand-set field as a table (Franklin 11-21: "almost spreadsheet fashion -- Starting Position | Name | Number |
// Kart Color | Skill ... Each row should be drag-and-drop to reorder -- starting position is filled by the actual row's
// location in the table"). Menu OKT > Field Roster opens it in its own window; the asset's Inspector shows the same table.
using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace OKT.EditorTools
{
    /// <summary>Draws one FieldRoster as a reorderable table.</summary>
    public class FieldRosterTable
    {
        readonly SerializedObject so;
        readonly ReorderableList list;
        const float StartW = 44f, NumW = 56f, ColorW = 90f, SkillW = 76f, Gap = 6f;
        static readonly string[] Skills = { "Auto", "Fast", "Mid", "New" };

        public FieldRosterTable(FieldRoster roster)
        {
            so = new SerializedObject(roster);
            var drivers = so.FindProperty("drivers");
            list = new ReorderableList(so, drivers, true, true, true, true)
            {
                elementHeight = EditorGUIUtility.singleLineHeight + 4f,
                drawHeaderCallback = DrawHeader,
                drawElementCallback = DrawRow,
                onAddCallback = AddRow,
            };
        }

        bool AsListed => so.FindProperty("gridOrder").enumValueIndex == (int)RosterGrid.AsListed;

        // column rects across one row (after the drag handle)
        static void Columns(Rect r, out Rect start, out Rect name, out Rect num, out Rect color, out Rect skill)
        {
            float x = r.x, y = r.y + 2f, h = EditorGUIUtility.singleLineHeight;
            float nameW = Mathf.Max(80f, r.width - StartW - NumW - ColorW - SkillW - 4 * Gap);
            start = new Rect(x, y, StartW, h); x += StartW + Gap;
            name = new Rect(x, y, nameW, h); x += nameW + Gap;
            num = new Rect(x, y, NumW, h); x += NumW + Gap;
            color = new Rect(x, y, ColorW, h); x += ColorW + Gap;
            skill = new Rect(x, y, SkillW, h);
        }

        void DrawHeader(Rect r)
        {
            r.xMin += 14f; // line up with the rows, past the drag handle
            Columns(r, out var start, out var name, out var num, out var color, out var skill);
            EditorGUI.LabelField(start, "Start", EditorStyles.boldLabel);
            EditorGUI.LabelField(name, "Name", EditorStyles.boldLabel);
            EditorGUI.LabelField(num, "Number", EditorStyles.boldLabel);
            EditorGUI.LabelField(color, "Kart Color", EditorStyles.boldLabel);
            EditorGUI.LabelField(skill, "Skill", EditorStyles.boldLabel);
        }

        void DrawRow(Rect r, int i, bool active, bool focused)
        {
            var e = list.serializedProperty.GetArrayElementAtIndex(i);
            Columns(r, out var start, out var name, out var num, out var color, out var skill);
            EditorGUI.LabelField(start, AsListed ? $"P{i + 1}" : "Q", EditorStyles.boldLabel);
            var pName = e.FindPropertyRelative("name"); pName.stringValue = EditorGUI.TextField(name, pName.stringValue);
            var pNum = e.FindPropertyRelative("number");
            var old = GUI.color; if (Duplicate(pNum.intValue, i)) GUI.color = new Color(1f, 0.55f, 0.55f);
            pNum.intValue = Mathf.Clamp(EditorGUI.IntField(num, pNum.intValue), 0, 99);
            GUI.color = old;
            var pCol = e.FindPropertyRelative("color"); pCol.colorValue = EditorGUI.ColorField(color, GUIContent.none, pCol.colorValue, true, false, false);
            var pTier = e.FindPropertyRelative("tier"); pTier.enumValueIndex = EditorGUI.Popup(skill, pTier.enumValueIndex, Skills);
        }

        bool Duplicate(int number, int row)
        {
            var d = list.serializedProperty;
            for (int j = 0; j < d.arraySize; j++)
                if (j != row && d.GetArrayElementAtIndex(j).FindPropertyRelative("number").intValue == number) return true;
            return false;
        }

        void AddRow(ReorderableList l)
        {
            var d = l.serializedProperty; int i = d.arraySize; d.arraySize++;
            var e = d.GetArrayElementAtIndex(i);
            var used = new HashSet<int>(); for (int j = 0; j < i; j++) used.Add(d.GetArrayElementAtIndex(j).FindPropertyRelative("number").intValue);
            int n = 1; while (used.Contains(n) && n < 99) n++;
            e.FindPropertyRelative("name").stringValue = "Driver " + (i + 1);
            e.FindPropertyRelative("number").intValue = n;
            e.FindPropertyRelative("color").colorValue = Color.HSVToRGB((i * 0.13f) % 1f, 0.8f, 0.9f);
            e.FindPropertyRelative("tier").enumValueIndex = (int)RosterTier.Auto;
            e.FindPropertyRelative("startPosition").intValue = 0;
            l.index = i;
        }

        public void Draw()
        {
            so.Update();
            EditorGUILayout.PropertyField(so.FindProperty("useRoster"), new GUIContent("Use this field", "Off = a made-up field each race"));
            var grid = so.FindProperty("gridOrder");
            int g = EditorGUILayout.Popup(new GUIContent("Starting grid", "Table order: drag rows to set who starts where (P1 = pole)."),
                                          grid.enumValueIndex == (int)RosterGrid.AsListed ? 0 : 1, new[] { "Table order (drag rows)", "By qualifying" });
            grid.enumValueIndex = g == 0 ? (int)RosterGrid.AsListed : (int)RosterGrid.Qualifying;
            EditorGUILayout.Space(4f);
            list.DoLayoutList();
            int count = list.serializedProperty.arraySize;
            EditorGUILayout.LabelField($"{count} karts. Skill Auto = the usual fast/mid/new mix by row. Red number = used twice.", EditorStyles.miniLabel);
            if (count < 2) EditorGUILayout.HelpBox("Needs at least 2 karts to race this field.", MessageType.Warning);
            so.ApplyModifiedProperties();
        }
    }

    [CustomEditor(typeof(FieldRoster))]
    public class FieldRosterEditor : Editor
    {
        FieldRosterTable table;
        public override void OnInspectorGUI()
        {
            if (table == null) table = new FieldRosterTable((FieldRoster)target);
            table.Draw();
            if (GUILayout.Button("Open in its own window")) FieldRosterWindow.Open();
        }
    }

    public class FieldRosterWindow : EditorWindow
    {
        FieldRosterTable table; FieldRoster roster; Vector2 scroll;

        [MenuItem("OKT/Field Roster")]
        public static void Open()
        {
            var w = GetWindow<FieldRosterWindow>("Field Roster");
            w.minSize = new Vector2(460f, 260f);
            w.Show();
        }

        void OnGUI()
        {
            if (roster == null) { roster = FieldRosterMenu.LoadOrMake(); table = null; }
            if (table == null) table = new FieldRosterTable(roster);
            EditorGUILayout.LabelField("The field for the next race (reset the race to use changes).", EditorStyles.miniLabel);
            scroll = EditorGUILayout.BeginScrollView(scroll);
            table.Draw();
            EditorGUILayout.EndScrollView();
        }
    }
}
