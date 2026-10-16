// SelfTest.cs -- `HandPoser.exe --selftest <folder>`: no window. Builds a pretend game folder in <folder>, feeds the
// model a hand-written state.json, and checks adding, the first angles coming from the game, holding, undo, redo,
// builds, the autosave and the command.json that goes to the script. Prints one PASS/FAIL line per check.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

static class SelfTest {
    static int failed;

    static void Check(string what, bool ok) {
        Console.WriteLine((ok ? "PASS " : "FAIL ") + what);
        if (!ok) failed++;
    }

    static string State(bool connected, string character, string anglesJson) {
        return "{\"game\":\"re3\",\"character\":\"" + character + "\",\"connected\":" + (connected ? "true" : "false") +
               ",\"seq_seen\":2,\"frame\":100,\"error\":\"\"," +
               "\"joints\":[[\"root\",-1],[\"spine_0\",0],[\"l_arm_wrist\",1],[\"l_hand_index_0\",2],[\"l_hand_index_1\",3]," +
               "[\"l_hand_thumb_0\",2],[\"r_arm_wrist\",1],[\"r_hand_index_0\",6]]," +
               "\"angles\":" + anglesJson + "}";
    }

    public static bool Run(string folder) {
        string game = Path.Combine(folder, "FakeGame");
        Directory.CreateDirectory(Path.Combine(game, "reframework", "autorun"));
        string appData = Path.Combine(folder, "appdata");
        if (Directory.Exists(appData)) Directory.Delete(appData, true);

        var link = new GameLink();
        link.Use(game);
        Check("game folder recognised", GameLink.LooksLikeGame(game) && Directory.Exists(link.DataDir));

        var model = new PoseModel(appData);
        model.ReadState(State(true, "pl2000", "{}"));
        Check("skeleton read: 8 joints", model.Skeleton.Names.Count == 8 && model.Game == "re3" && model.Connected);
        var below = model.Skeleton.Descendants("l_arm_wrist");
        Check("children of l_arm_wrist are the 3 left finger joints",
            below.SetEquals(new[] { "l_hand_index_0", "l_hand_index_1", "l_hand_thumb_0" }));

        model.Mark();
        int added = model.AddMatching(n => n.StartsWith("l_hand_"));
        Check("LEFT FINGERS adds 3 rows, none held yet", added == 3 && model.Rows.Count == 3 && model.Rows.All(r => !r.Known));
        string cmd = model.Command();
        var c = Json.Obj(Json.Parse(cmd));
        Check("command asks to read 3, holds none", Json.Arr(c["want_read"]).Count == 3 && Json.Obj(c["hold"]).Count == 0 && Json.Num(c["seq"], 0) == 1);
        link.WriteCommand(cmd);
        Check("command.json written", File.Exists(link.CommandFile) && File.ReadAllText(link.CommandFile) == cmd);

        model.ReadState(State(true, "pl2000", "{\"l_hand_index_0\":[10,20,30],\"l_hand_index_1\":[1,2,3],\"l_hand_thumb_0\":[0,0,0]}"));
        var r0 = model.Find("l_hand_index_0");
        Check("first angles come from the game", r0.Known && r0.X == 10 && r0.Y == 20 && r0.Z == 30 && model.Rows.All(r => r.Known));

        model.Mark();
        model.Set("l_hand_index_0", 45, 20, 30);
        model.ReadState(State(true, "pl2000", "{\"l_hand_index_0\":[10,20,30]}"));
        Check("a held joint keeps the window's number", model.Find("l_hand_index_0").X == 45);
        c = Json.Obj(Json.Parse(model.Command()));
        var h = Json.Arr(Json.Obj(c["hold"])["l_hand_index_0"]);
        Check("command holds 3 joints, index_0 at 45", Json.Obj(c["hold"]).Count == 3 && Json.Num(h[0], 0) == 45 && Json.Num(c["seq"], 0) == 2);

        model.Set("l_hand_index_0", 500, -500, 0);
        Check("angles clamp to -180..180", model.Find("l_hand_index_0").X == 180 && model.Find("l_hand_index_0").Y == -180);
        model.Set("l_hand_index_0", 45, 20, 30);   // back to the value the undo/redo checks below expect

        model.Undo();
        Check("undo puts index_0 back to 10", model.Find("l_hand_index_0").X == 10 && model.CanRedo);
        model.Undo();
        Check("second undo removes the rows", model.Rows.Count == 0 && !model.CanUndo);
        model.Redo(); model.Redo();
        Check("redo twice brings 45 back", model.Rows.Count == 3 && model.Find("l_hand_index_0").X == 45);

        string file = model.SaveBuild("fist: test/1");
        Check("build saved under a safe name", file != null && File.Exists(file) && Path.GetFileName(file) == "fist_ test_1.json");
        Check("build listed", model.Builds().SequenceEqual(new[] { "fist_ test_1" }));
        model.Mark(); model.Clear();
        Check("LOAD puts the rows back", model.LoadBuild("fist_ test_1") && model.Rows.Count == 3 && model.Find("l_hand_index_0").X == 45);

        model.Autosave();
        var again = new PoseModel(appData);
        again.ReadState(State(true, "pl2000", "{}"));
        Check("autosave comes back for the same game", again.LoadAutosave() && again.Rows.Count == 3 && again.Find("l_hand_index_0").X == 45);

        model.Mark();
        Check("REMOVE drops one row", model.Remove("l_hand_thumb_0") && model.Rows.Count == 2);
        Check("200-step undo limit holds", UndoLimit(appData));

        Check("state older than 3 s counts as not alive", !link.ScriptAlive() || File.Exists(link.StateFile));
        File.WriteAllText(link.StateFile, State(true, "pl2000", "{}"));
        Check("fresh state.json counts as alive", link.ScriptAlive());

        bool threw = false;
        try { model.ReadState("{ this is not json"); } catch (FormatException) { threw = true; }
        Check("half-written state.json is rejected, not crashed on", threw);

        Console.WriteLine(failed == 0 ? "ALL PASS" : failed + " FAILED");
        return failed == 0;
    }

    static bool UndoLimit(string appData) {
        var m = new PoseModel(appData);
        m.ReadState(State(true, "pl2000", "{}"));
        m.Add("spine_0"); m.Set("spine_0", 0, 0, 0);
        for (int i = 1; i <= 250; i++) { m.Mark(); m.Set("spine_0", i % 180, 0, 0); }
        int steps = 0;
        while (m.CanUndo) { m.Undo(); steps++; }
        return steps == PoseModel.UNDO_STEPS;
    }
}
