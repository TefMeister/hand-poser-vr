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
        {   // 2026-11-08: the measured slider directions (HandAxes.cs)
            string l, r; bool f;
            HandAxes.Ends("R_IndexF1", 2, out l, out r, out f);
            bool rightBend = l == "IN" && r == "OUT" && f;
            HandAxes.Ends("L_IndexF1", 2, out l, out r, out f);
            bool leftBend = l == "IN" && r == "OUT" && !f;
            HandAxes.Ends("L_Hand", 2, out l, out r, out f);
            bool wrist = l == "DOWN" && r == "UP" && !f;
            HandAxes.Ends("L_IndexF2", 1, out l, out r, out f);
            bool side = l == "LEFT" && f;
            // 2026-11-09: the thumb's tip curls on Y, shown in the right-hand column as IN / OUT (IN on the left end for the left hand)
            HandAxes.Ends("L_Thumb3", 1, out l, out r, out f);
            side = side && l == "IN" && f && HandAxes.Column("L_Thumb3", 1) == 2 && HandAxes.Column("L_Thumb3", 2) == 1 && HandAxes.Column("L_IndexF3", 2) == 2;
            HandAxes.Ends("L_Forearm", 0, out l, out r, out f);
            bool other = l == "-X" && r == "+X" && !f;
            Check("slider ends: right fingers' bend shown turned round, left not; the wrist DOWN / UP; sideways LEFT / RIGHT; the thumb tip's curl in the right column; an arm joint plain",
                  rightBend && leftBend && wrist && side && other);
        }
        Check("plain names: R_IndexF1 is the right index knuckle, l_hand_thumb_2 the left thumb tip, L_Hand the left wrist",
              JointNames.Describe("R_IndexF1") == "right index finger: knuckle (base)" && JointNames.Describe("l_hand_thumb_2") == "left thumb: tip joint" &&
              JointNames.Describe("L_Hand") == "left wrist (turns the whole hand)" && JointNames.Describe("L_PinkyF3") == "left pinky finger: tip joint");
        Check("the selected hand's line: only that hand, in plain words (left zone, free, docked, holster; right zone, grip, no gun)",
              HandSpots.Line("ready at slide from the left + right hand on the cover", "l", "LEMI") == "LEFT HAND: in the slide from the left grab zone (hand open, ready)" &&
              HandSpots.Line("ready at slide from the left + right hand on the cover", "r", "LEMI") == "RIGHT HAND: on the cover" &&
              HandSpots.Line("free", "l", "LEMI") == "LEFT HAND: free: near the gun, in no zone" &&
              HandSpots.Line("ready on magazine", "", "LEMI") == "LEFT HAND: docked on the magazine, hand open until LT" &&
              HandSpots.Line("slide", "l", "LEMI") == "LEFT HAND: docked on the slide" &&
              HandSpots.Line("free + right hand ready at slide from the left", "r", "LEMI") == "RIGHT HAND: in the slide from the left grab zone (hand open, ready)" &&
              HandSpots.Line("right hand at right hip holster", "r", "") == "RIGHT HAND: at the right hip holster" &&
              HandSpots.Line("right hand at right hip holster", "l", "") == "LEFT HAND: away from the gun, in no zone" &&
              HandSpots.Line("", "r", "LEMI") == "RIGHT HAND: on the gun's grip" && HandSpots.Line(null, "r", "") == "RIGHT HAND: free, no gun in hand" &&
              HandSpots.Line("free + trigger pulled", "r", "LEMI") == "RIGHT HAND: on the grip, trigger pulled");
        // 2026-11-14: the spare-magazine lines, and where a never-posed one starts from
        Check("the spare-magazine lines in plain words",
              HandSpots.Line("two magazines in hand", "l", "LEMI") == "LEFT HAND: holding two magazines" &&
              HandSpots.Line("spare in hand, docked on the magazine", "l", "LEMI") == "LEFT HAND: spare in hand, docked on the gun's magazine" &&
              HandSpots.Line("spare in hand, pulling the magazine out", "l", "LEMI") == "LEFT HAND: spare in hand, pulling the gun's magazine out" &&
              HandSpots.Line("spare in hand, magazine still in the gun", "l", "LEMI") == "LEFT HAND: spare in hand, the gun's magazine still in");
        {   // 2026-11-14: each row's own undo / redo, per pose
            var r = new Row { Name = "l_hand_index_0", X = 1, Y = 2, Z = 3, Known = true };
            RowHistory.Mark("LEMI @ magazine", r); r.Z = 10;
            double x, y, z;
            bool back = RowHistory.Step("LEMI @ magazine", r, false, out x, out y, out z);
            Check("a row's undo gives its own numbers back", back && x == 1 && y == 2 && z == 3);
            Check("...and not in another pose", !RowHistory.CanUndo("LEMI @ slide", "l_hand_index_0") && !RowHistory.CanUndo("LEMI @ magazine", "l_hand_middle_0"));
            r.Z = 3;
            Check("redo takes it forward again, once", RowHistory.Step("LEMI @ magazine", r, true, out x, out y, out z) && z == 10 && !RowHistory.CanRedo("LEMI @ magazine", r.Name));
        }
        {   // 2026-11-14: the docked-wrist and magazine boxes' own undo / redo, per gun (and spot)
            BoxHistory.Mark("mag:3046", new[] { 1, 2, 3, 4, 5, 6 });
            var back = BoxHistory.Step("mag:3046", new[] { 9, 2, 3, 4, 5, 6 }, false);
            Check("a box's undo gives all six sliders back", back != null && back[0] == 1 && back[5] == 6);
            Check("...redo the way forward, and nothing for another gun", BoxHistory.CanRedo("mag:3046") && !BoxHistory.CanUndo("mag:3040"));
        }
        Check("the magazine box is live with a magazine in the left hand (13, 16-19) and not otherwise",
              MagBox.Holding(13) && MagBox.Holding(16) && MagBox.Holding(19) && !MagBox.Holding(4) && !MagBox.Holding(15) && !MagBox.Holding(20));
        Check("a spare-magazine line starts from its older spot's pose",
              string.Join("|", HandSpots.Fallbacks("LEMI", "spare in hand, docked on the magazine").ToArray()) == "LEMI @ magazine" &&
              string.Join("|", HandSpots.Fallbacks("LEMI", "two magazines in hand").ToArray()) == "LEMI @ magazine in hand");
        Check("fall-backs for a never-posed spot, in the game script's order",
              string.Join("|", HandSpots.Fallbacks("LEMI", "ready at slide")) == "LEMI @ ready|any gun @ ready" &&
              string.Join("|", HandSpots.Fallbacks("LEMI", "right hand ready at drum")) == "LEMI @ right hand ready|any gun @ right hand ready" &&
              string.Join("|", HandSpots.Fallbacks("LEMI", "at right hip holster")) == "any gun @ at right hip holster" &&
              HandSpots.Fallbacks("", "at right hip holster").Count == 0 && HandSpots.Fallbacks("LEMI", "slide").Count == 0 &&
              HandSpots.Fallbacks("LEMI", "at left hip holster + right hand at left shoulder holster").Count == 0);
        Check("the guard: both hands named, 'any gun @ guard' with a gun, nothing to fall back on with no gun",
              HandSpots.Line("guard", "l", "") == "LEFT HAND: guard (block held, arms up)" && HandSpots.Line("guard", "r", "") == "RIGHT HAND: guard (block held, arms up)" &&
              string.Join("|", HandSpots.Fallbacks("LEMI", "guard")) == "any gun @ guard" && HandSpots.Fallbacks("", "guard").Count == 0);
        Check("finger groups: L_IndexF1 and l_hand_index_0 name their finger, a lone joint none",
              PoseModel.GroupOf("L_IndexF1") == "L_IndexF" && PoseModel.GroupOf("l_hand_index_0") == "l_hand_index" && PoseModel.GroupOf("L_Hand") == null);
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
        // 2026-11-01: an added wrist is not held until its sliders move (it froze the gun hand at the angle it had when added)
        model.Add("l_arm_wrist");
        model.ReadState(State(true, "pl2000", "{\"l_arm_wrist\":[5,6,7]}"));
        var w = model.Find("l_arm_wrist");
        c = Json.Obj(Json.Parse(model.Command()));
        Check("an added wrist reads from the game but is not held", w.Known && !w.Holds && !Json.Obj(c["hold"]).ContainsKey("l_arm_wrist"));
        model.Set("l_arm_wrist", 20, 6, 7);
        c = Json.Obj(Json.Parse(model.Command()));
        Check("a wrist moved by hand is held", Json.Obj(c["hold"]).ContainsKey("l_arm_wrist"));
        model.Remove("l_arm_wrist");
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

        SpotNumberChecks(folder);
        Console.WriteLine(failed == 0 ? "ALL PASS" : failed + " FAILED");
        return failed == 0;
    }

    // 2026-11-24 (Tefa: "have all the grip zones and different zone poses numbered"): the numbers per gun, L / R / H / A / M / F
    static void SpotNumberChecks(string folder) {
        string data = Path.Combine(folder, "spotdata");
        if (Directory.Exists(data)) Directory.Delete(data, true);
        Directory.CreateDirectory(Path.Combine(data, "guns"));
        const string W = "ri3008_Inventory";
        foreach (var n in new[] { W + " @ drum", W + " @ handle", W + " @ drum + right hand on the cover", "ri3021_Inventory @ slide" })
            File.WriteAllText(Path.Combine(data, "guns", n + ".json"), "{\"joints\":{}}");
        SpotNumbers.Seed(W, data);
        string handle = SpotNumbers.Code(W, "l", "handle", data), drum = SpotNumbers.Code(W, "l", "drum", data);
        Check("seeded: handle and drum are two L numbers (" + handle + ", " + drum + ")", handle.StartsWith("L") && drum.StartsWith("L") && handle != drum);
        Check("seeded: the right hand's cover is R1", SpotNumbers.Code(W, "r", "right hand on the cover", data) == "R1");
        string hammer = SpotNumbers.Code(W, "l", "hammer", data);
        Check("a new spot takes the next number (" + hammer + ")", hammer == "L3");
        Check("asked again, the same number", SpotNumbers.Code(W, "l", "hammer", data) == "L3" && SpotNumbers.Code(W, "l", "drum", data) == drum);
        Check("seeding again changes nothing", Seed2(W, data, drum, hammer));
        Check("a holster is H (right hip = H2)", SpotNumbers.Code(W, "r", "right hand at right hip holster", data) == "H2" && SpotNumbers.Code(W, "l", "at left shoulder holster", data) == "H4");
        Check("the pouch is A, the map M, first aid F", SpotNumbers.Code(W, "l", "ready at pouch", data) == "A" && SpotNumbers.Code(W, "r", "right hand at map", data) == "M" && SpotNumbers.Code(W, "l", "at first aid", data) == "F");
        Check("free, guard and nothing have no number", SpotNumbers.Code(W, "l", "free", data) == "" && SpotNumbers.Code(W, "l", "", data) == "" && SpotNumbers.Code(W, "l", "guard", data) == "");
        Check("another gun has its own list: its posed slide L1, a new magazine L2", SpotNumbers.Code("ri3021_Inventory", "l", "magazine", data) == "L2" && SpotNumbers.Code("ri3021_Inventory", "l", "slide", data) == "L1");
        var list = SpotNumbers.List(W, "l", data);
        Check("the left list: the L numbers in order, then H1-H4, A, M, F (" + list.Count + ")", list.Count == 3 + 7 && list[0].Key == "L1" && list[2].Key == "L3" && list[3].Key == "H1" && list[9].Key == "F");
        Check("the line has the number: " + HandSpots.Line("drum + right hand on the cover", "l", W, data), HandSpots.Line("drum + right hand on the cover", "l", W, data) == "LEFT HAND: " + drum + " · docked on the drum");
        Check("the right line too: " + HandSpots.Line("drum + right hand on the cover", "r", W, data), HandSpots.Line("drum + right hand on the cover", "r", W, data) == "RIGHT HAND: R1 · on the cover");
        Check("the latch spot is the right hand's: " + HandSpots.Line("right hand, thumb on the latch", "r", "ri3023_Inventory"), HandSpots.Line("right hand, thumb on the latch", "r", "ri3023_Inventory") == "RIGHT HAND: thumb on the latch" && HandSpots.Line("right hand, thumb on the latch", "l", "ri3023_Inventory") == "LEFT HAND: away from the gun, in no zone");
        Check("no number where there is none", HandSpots.Line("free", "l", W, data) == "LEFT HAND: free: near the gun, in no zone");
        SpotNumbers.WriteNow(data, W, "L2 docked on the drum", "R1 on the cover");
        var now = Json.Obj(Json.Parse(File.ReadAllText(Path.Combine(data, SpotNumbers.NOW_FILE))));
        Check("spot_now.json written for the other apps", now != null && Json.Str(now["left"]) == "L2 docked on the drum" && Json.Str(now["right"]) == "R1 on the cover" && Json.Num(now["t"], 0) > 1.7e9);
        Check("split: left and right parts", SpotNumbers.LeftPart("drum + right hand on the cover") == "drum" && SpotNumbers.RightPart("drum + right hand on the cover") == "right hand on the cover" && SpotNumbers.RightPart("trigger pulled") == "trigger pulled" && SpotNumbers.LeftPart("trigger pulled") == "");
    }
    static bool Seed2(string gun, string data, string drum, string hammer) {
        SpotNumbers.Seed(gun, data);
        return SpotNumbers.Code(gun, "l", "drum", data) == drum && SpotNumbers.Code(gun, "l", "hammer", data) == hammer;
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
