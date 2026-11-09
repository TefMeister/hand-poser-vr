// LeftHand.cs -- the free left hand's whereabouts in plain words for the window's top line (2026-11-09). Kept apart from
// PoserUi.cs, which is at its size limit.
using System;

static class LeftHand {
    const string AT = "ready at ", ON = "ready on ";   // the game script's names for a ready hand in an area / on a part
    // 2026-11-09 (Tefa, in the headset: "make Hand Poser say in the info part that the left hand is now in for example left side
    // slide activation area, so i know to freeze it and start working on it"): the free left hand's whereabouts in plain words,
    // from the spot the game script names ("ready at slide from the left", "ready on slide", "ready", "free"; Visceral RE8 slot 213)
    public static string Words(string spot) {
        string sp = (spot ?? "").Split(new[] { " + " }, StringSplitOptions.None)[0];   // the left hand's half of the spot
        if (sp.StartsWith(AT)) return " · left hand: ready, in the " + sp.Substring(AT.Length) + " area";
        if (sp.StartsWith(ON)) return " · left hand: ready, docked on the " + sp.Substring(ON.Length) + " (LT closes it)";
        if (sp == "ready") return " · left hand: ready, in an area with no name yet";
        if (sp == "free") return " · left hand: free";
        return "";
    }
}
