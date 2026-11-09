// HandSpots.cs -- where the selected hand is, in plain words, on its own big line at the top of the window (2026-11-09). Kept
// apart from PoserUi.cs, which is at its size limit. The spot names come from the game script (hand_poser.lua), with Visceral
// RE8's bridge slots 153, 184 and 213-216 and praydog's block.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

static class HandSpots {
    const string READY_AT = "ready at ", READY_ON = "ready on ", AT = "at ";   // the game script's names for the left hand
    const string RIGHT = "right hand ";                                        // every right-hand spot starts with this
    const string TRIGGER = "trigger pulled";                                   // the right hand's, without the prefix
    const string START = "start ", LAND = "land ";                             // a grab sphere / a landing spot, with its pair
    public const string GUARD = "guard";   // 2026-11-09: Village's block, both hands in one spot ("<gun> @ guard")
    const float LINE_FONT_PT = 17f;        // big enough to read through Window View in the headset
    const int LINE_HEIGHT_PX = 40;

    // 2026-11-09 (Tefa: "make it a bit bigger and clearer to understand where my hand is exactly and show only left hand when left
    // hand is selected ... same for the right hand"): its own line at the top, still text (no animation in helper windows)
    public static SkinLabel MakeLine(Color ink) {
        return new SkinLabel { Dock = DockStyle.Top, Height = LINE_HEIGHT_PX, Padding = new Padding(8, 6, 0, 0), ForeColor = ink,
                               Font = new Font("Segoe UI Semibold", LINE_FONT_PT), AutoEllipsis = true };
    }

    // the line for one hand: side "r" = the right, anything else the left (a window with no hand picked yet shows the left)
    public static string Line(string spot, string side, string gun) {
        bool right = side == "r";
        string left = "", rpart = "";
        foreach (var part in (spot ?? "").Split(new[] { " + " }, StringSplitOptions.None)) {
            if (part.StartsWith(RIGHT) || part == TRIGGER) rpart = part; else if (part != "" && left == "") left = part;
        }
        if (spot == GUARD) { left = GUARD; rpart = GUARD; }
        return (right ? "RIGHT HAND: " + RightPlace(rpart, gun) : "LEFT HAND: " + LeftPlace(left));
    }

    static string LeftPlace(string sp) {
        if (sp == GUARD) return "guard (block held, arms up)";
        if (sp == "free") return "free: near the gun, in no zone";
        if (sp == "ready") return "in a grab zone with no name yet (hand open)";
        if (sp.StartsWith(READY_AT)) return "in the " + sp.Substring(READY_AT.Length) + " grab zone (hand open, ready)";
        if (sp.StartsWith(READY_ON)) return "docked on the " + sp.Substring(READY_ON.Length) + ", hand open until LT";
        if (sp.StartsWith(AT)) return "at the " + sp.Substring(AT.Length);
        if (sp.StartsWith(START)) return "in grab sphere " + sp.Substring(START.Length);
        if (sp.StartsWith(LAND)) return "landed on the gun, pair " + sp.Substring(LAND.Length);
        if (sp == "magazine in hand") return "holding a magazine";
        if (sp == "rounds from the pouch") return "holding rounds from the pouch";
        if (sp == "") return "away from the gun, in no zone";
        return "docked on the " + sp;   // slide, magazine, handle, drum, insert point, hammer, "... at the back"
    }
    static string RightPlace(string sp, string gun) {
        if (sp == GUARD) return "guard (block held, arms up)";
        if (sp == TRIGGER) return "on the grip, trigger pulled";
        if (sp == "") return string.IsNullOrEmpty(gun) ? "free, no gun in hand" : "on the gun's grip";
        string p = sp.Substring(RIGHT.Length);
        if (p.StartsWith(READY_AT)) return "in the " + p.Substring(READY_AT.Length) + " grab zone (hand open, ready)";
        if (p.StartsWith(AT)) return "at the " + p.Substring(AT.Length);
        return p;   // "on the cover", "on the bolt", "over the hammer", "on the bolt at the back"
    }

    // the pose files to start a never-posed spot from, most specific first, the same order the game script loads them in
    // (hand_poser.lua hand_chain): "<gun> @ ready" (or "right hand ready") then "any gun @ ready"; a holster or the guard "any gun
    // @ ..." (none with bare hands); "free" "any gun @ free". A spot of both hands ("... + right hand ...") has none.
    public static List<string> Fallbacks(string gun, string spot) {
        var list = new List<string>();
        string g = string.IsNullOrEmpty(gun) ? "no gun" : gun;
        if (string.IsNullOrEmpty(spot) || spot.Contains(" + ")) return list;
        string side = spot.StartsWith(RIGHT) ? RIGHT : "", p = spot.Substring(side.Length);
        string plain = p.StartsWith("ready") ? "ready" : (p == "free" && side == "") ? "free" : null;
        if (plain != null) {
            if (plain != p) list.Add(g + " @ " + side + plain);
            list.Add("any gun @ " + side + plain);
        } else if ((p.StartsWith(AT) || p == GUARD) && g != "no gun") list.Add("any gun @ " + spot);
        return list;
    }
}
