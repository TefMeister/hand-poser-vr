// HandSpots.cs -- where each hand is, in plain words, for the window's top line (2026-11-09). Kept apart from PoserUi.cs, which is
// at its size limit. The spot names come from the game script (hand_poser.lua), with Visceral RE8's bridge slots 213-216.
using System;
using System.Collections.Generic;

static class HandSpots {
    const string READY_AT = "ready at ", READY_ON = "ready on ", AT = "at ";   // the game script's names for the left hand
    const string RIGHT = "right hand ";                                        // every right-hand spot starts with this
    public const string GUARD = "guard";   // 2026-11-09: Village's block, both hands in one spot ("<gun> @ guard")

    // 2026-11-09 (Tefa, in the headset: "make Hand Poser say in the info part that the left hand is now in for example left side
    // slide activation area, so i know to freeze it and start working on it"; later: "all the grip zones ... and holster zones for
    // both left and right hand"): the spot is "<left part> + <right part>", either part may be missing
    public static string Words(string spot) {
        if (spot == GUARD) return " · left hand: guard · right hand: guard";
        string left = "", right = "";
        foreach (var part in (spot ?? "").Split(new[] { " + " }, StringSplitOptions.None)) {
            if (part.StartsWith(RIGHT)) right = part.Substring(RIGHT.Length); else if (part != "" && left == "") left = part;
        }
        return LeftWords(left) + RightWords(right);
    }
    static string LeftWords(string sp) {
        if (sp.StartsWith(READY_AT)) return " · left hand: ready, in the " + sp.Substring(READY_AT.Length) + " area";
        if (sp.StartsWith(READY_ON)) return " · left hand: ready, docked on the " + sp.Substring(READY_ON.Length) + " (LT closes it)";
        if (sp.StartsWith(AT)) return " · left hand: at the " + sp.Substring(AT.Length);
        if (sp == "ready") return " · left hand: ready, in an area with no name yet";
        if (sp == "free") return " · left hand: free";
        return "";
    }
    static string RightWords(string sp) {   // sp without "right hand "
        if (sp.StartsWith(READY_AT)) return " · right hand: ready, in the " + sp.Substring(READY_AT.Length) + " area";
        if (sp.StartsWith(AT)) return " · right hand: at the " + sp.Substring(AT.Length);
        return "";
    }

    // the pose files to start a never-posed spot from, most specific first, the same order the game script loads them in
    // (hand_poser.lua hand_chain): "<gun> @ ready" (or "right hand ready") then "any gun @ ready"; a holster "any gun @ at
    // <holster>" (none with bare hands); "free" "any gun @ free". A spot of both hands ("... + right hand ...") has none.
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
