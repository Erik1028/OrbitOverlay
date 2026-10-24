using System;
using System.Threading;

namespace Orbit;

// The friends widget and the evening's recap, the app's side (Sys/Friends.cs, Sys/SessionRecap.cs; the watch: WristFriends.cs,
// WristRecap.cs). VRCX's friends are read only while a friends widget is on a watch page and the watch can be seen (SteamVR runs, or
// the settings show the watch) - else its thread sleeps. The session is followed whenever SteamVR runs; a recap is finished (its
// photos counted, recap.json written) on a thread of its own, then shown as a card - or, SteamVR gone, put in the Notifications list.
// Nothing here allocates at rest.
public sealed partial class OrbitApp
{
    readonly FriendsWatcher _friends = new();
    readonly SessionTracker _session = new();
    SessionRecap _recapDone;                     // finished on its thread, not shown yet
    bool _recapDoneCard, _recapLoaded;
    Thread _recapThread;

    void UpdateSocial(double now)
    {
        bool want = C.Wrist.Face == "custom" && WatchPages.Any(C.Wrist, "friends") && (VrAfter(4) || now - _photoPreview < 3);
        if (want)
        {
            _friends.VrcxDb = _room.VrcxDb;
            _friends.MeId = _roomSnap.MeId.Length > 0 ? _roomSnap.MeId : null;
        }
        _friends.Active = want;
        _data.Friends = want ? _friends.Get() : null;
        // the last recap from the file, once (off the frame)
        if (!_recapLoaded)
        {
            _recapLoaded = true;
            ThreadPool.QueueUserWorkItem(_ => { if (RecapStore.Load(RecapStore.DefaultPath) is { } r) Volatile.Write(ref _recapDone, r); });
        }
        var ended = _session.Tick(DateTime.Now, _vr.Connected, _vr.HeadsetWorn, C.Room.Enabled ? _roomSnap : null, out bool card);
        if (ended != null) FinishRecap(ended, card, false);
        if (Volatile.Read(ref _recapDone) is { } done)
        {
            Volatile.Write(ref _recapDone, null);
            bool fresh = _data.Recap == null || done.End > _data.Recap.End;
            if (fresh) _data.Recap = done;
            if (fresh && done.End > DateTime.Now.AddMinutes(-30)) RecapCard(done, _recapDoneCard);     // (the one from the file: no card)
            _recapDoneCard = false;
        }
    }

    // its photos counted and the file written on a thread of its own (wait: at the exit, here and now)
    void FinishRecap(SessionRecap r, bool card, bool wait)
    {
        Log.Info($"recap: a session of {r.Length.TotalMinutes:0} min ended ({(card ? "headset off" : "SteamVR gone")}), {r.WorldCount} world(s), " +
                 $"{r.People} people");
        if (wait) { RecapStore.Finish(r, PhotoWatcher.PictureFolder(), RecapStore.DefaultPath); _data.Recap = r; return; }
        _recapThread = new Thread(() =>
        {
            RecapStore.Finish(r, PhotoWatcher.PictureFolder(), RecapStore.DefaultPath);
            _recapDoneCard = card;
            Volatile.Write(ref _recapDone, r);
        }) { IsBackground = true, Name = "recap" };
        _recapThread.Start();
    }

    // the card (the overlay there: shown, and so in the Notifications list); SteamVR gone: in the list only
    void RecapCard(SessionRecap r, bool card)
    {
        string title = Lang.F("Session recap · {0} in VR", WristPanel.RecapLength(r.Length));
        string text = WristPanel.RecapLine(r);
        if (card && _vr.Connected)
            _notifyIn.Incoming.Enqueue(new Notification { Source = "OrbitOverlay", Title = title, Content = text, Timeout = 15, Volume = 0.5f });
        else _notes.Add("OrbitOverlay", title, text, r.End);
    }

    // the exit: a session still open is a session that ended (its recap written here and now)
    void SocialDispose()
    {
        var r = _session.Tick(DateTime.Now, false, false, null, out _);
        if (r != null) FinishRecap(r, false, true);
        _recapThread?.Join(3000);
        _friends.Dispose();
    }
}
