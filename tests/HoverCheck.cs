using System;
using System.Drawing;
using System.Drawing.Imaging;
internal static class HoverCheck
{
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    [STAThread] static void Main()
    {
        var state = new HoverIntent();
        Check(state.Update(true, false, false, 0) == 0, "no instant popup");
        Check(state.Update(true, false, false, 160) == 0, "entry delay");
        Check(state.Update(true, false, false, 240) == 1, "hover opens");
        Check(state.Update(false, false, true, 300) == 0, "bridge gap grace");
        Check(state.Update(false, true, true, 450) == 0, "card stays open");
        Check(state.Update(false, false, true, 500) == 0, "leave delay");
        Check(state.Update(false, false, true, 800) == -1, "leave closes");
        state.Dismiss();
        Check(state.Update(true, false, false, 1000) == 0, "dismiss suppresses reopen");
        Check(state.Update(true, false, false, 1400) == 0, "dismiss stays suppressed");
        state.Update(false, false, false, 1500);
        state.Update(true, false, false, 1600);
        Check(state.Update(true, false, false, 1850) == 1, "reentry opens again");
        Console.WriteLine("PASS: hover delay, card transit, leave, dismissal, reentry");
    }
}
