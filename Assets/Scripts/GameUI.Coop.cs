using UnityEngine;

/// <summary>The CO-OP page: connect, create / join a room by code, friends list, invites.</summary>
public partial class GameUI
{
    string coopCode = "", coopFriend = "";
    Vector2 coopScroll;

    void CoopTick()
    {
        if (Coop.PendingStart && tPhase == 0 && GameState.Mode == GameMode.Menu)
        {
            Coop.PendingStart = false;
            StartTransit("CO-OP RUN", Coop.BeginRun);
        }
        // after a co-op round, go back to the lobby while you are still in the room
        if (Coop.WantLobby && page == Page.Main && GameState.Mode == GameMode.Menu) { page = Page.Coop; Coop.WantLobby = false; }
        else if (Coop.WantLobby && GameState.Mode == GameMode.Menu && page != Page.Main && page != Page.Coop) Coop.WantLobby = false;
    }

    void LeaveCoopPage()
    {
        if (Coop.InRoom) Coop.Cmd("leave");
        page = Page.Main;
    }

    static string SuitName(int suit)
    {
        return Suits.VariantNames[Suits.VariantOf(suit)] + " " + GameSettings.CostumeNames[Suits.CostumeOf(suit)];
    }

    void DrawInviteToast(float W, float H)
    {
        if (Coop.InviteUntil <= Time.unscaledTime || Coop.InviteFrom.Length == 0) return;
        var r = new Rect(W / 2 - 300, 14, 600, 64);
        GUI.Box(r, "", box);
        Rect(new Rect(r.x, r.y, 6, r.height), new Color(0.35f, 0.7f, 1f));
        Text(new Rect(r.x + 20, r.y + 8, 380, 48), Coop.InviteFrom.ToUpper() + " INVITES YOU TO CO-OP", label, Color.white, TextAnchor.MiddleLeft, 13);
        if (Button(new Rect(r.x + 410, r.y + 14, 100, 36), "JOIN", btnSmall)) { Coop.AcceptInvite(); page = Page.Coop; }
        if (Button(new Rect(r.x + 520, r.y + 14, 62, 36), "X", btnSmall)) Coop.InviteUntil = 0f;
    }

    void DrawCoop(float W, float H)
    {
        Dim(W, H, 0.78f);
        Rect r = Center(W, H, 900, 610);
        GUI.Box(r, "", box);
        Text(new Rect(r.x, r.y + 12, r.width, 40), "CO-OP", title, Gold, TextAnchor.MiddleCenter, 28);
        Text(new Rect(r.x, r.y + 52, r.width, 22), "2 PLAYERS  -  SHARED HEARTS  -  GOLD RINGS (HOST)  +  BLUE RINGS (GUEST)", small, new Color(1, 1, 1, 0.55f), TextAnchor.MiddleCenter);

        float lx = r.x + 30, lw = 400, rx = r.x + 470, rw = 400;
        Color blue = new Color(0.45f, 0.75f, 1f);

        // ---------------------------------------------------------------- left: connection + room
        if (Net.State != AccountState.LoggedIn)
        {
            Text(new Rect(lx, r.y + 130, lw, 60), "SIGN IN TO AN ONLINE ACCOUNT TO PLAY CO-OP.", label, new Color(1f, 0.7f, 0.3f), TextAnchor.MiddleLeft, 12);
        }
        else if (Coop.Conn == CoopConn.Off)
        {
            Text(new Rect(lx, r.y + 100, lw, 24), "NOT CONNECTED", label, new Color(1f, 0.6f, 0.4f), TextAnchor.MiddleLeft, 13);
            if (Button(new Rect(lx, r.y + 134, lw, 52), "CONNECT")) Coop.Connect();
        }
        else if (Coop.Conn == CoopConn.Connecting)
        {
            Text(new Rect(lx, r.y + 100, lw, 24), "CONNECTING" + new string('.', 1 + (int)(Time.unscaledTime * 2f) % 3), label, blue, TextAnchor.MiddleLeft, 13);
            Text(new Rect(lx, r.y + 130, lw, 60), "THE FREE SERVER SLEEPS WHEN NOBODY PLAYS - THE FIRST CONNECTION CAN TAKE UP TO A MINUTE. (" + Mathf.FloorToInt(Coop.ConnectingFor) + "s)", small, new Color(1, 1, 1, 0.6f), TextAnchor.UpperLeft);
        }
        else if (!Coop.InRoom)
        {
            Text(new Rect(lx, r.y + 96, lw, 24), "ONLINE AS " + Net.User.ToUpper(), label, Color.green, TextAnchor.MiddleLeft, 12);
            if (Button(new Rect(lx, r.y + 130, lw, 56), "CREATE ROOM")) Coop.Cmd("create");
            Text(new Rect(lx, r.y + 204, lw, 22), "OR JOIN A FRIEND WITH THEIR CODE", small, new Color(1, 1, 1, 0.6f));
            coopCode = Field(new Rect(lx, r.y + 232, 250, 44), coopCode, 6, false, "ROOM CODE").ToUpper();
            GUI.enabled = coopCode.Trim().Length >= 4;
            if (Button(new Rect(lx + 262, r.y + 232, 138, 44), "JOIN")) Coop.Cmd("join", "code", coopCode.Trim());
            GUI.enabled = true;
        }
        else
        {
            Text(new Rect(lx, r.y + 92, lw, 22), "ROOM CODE", small, new Color(1, 1, 1, 0.6f));
            Text(new Rect(lx, r.y + 112, lw, 70), Coop.RoomCode, title, Gold, TextAnchor.MiddleLeft, 56);
            for (int i = 0; i < 2; i++)
            {
                var row = new Rect(lx, r.y + 196 + i * 46, lw, 40);
                Rect(row, new Color(0.12f, 0.1f, 0.18f, 0.95f));
                if (i < Coop.Members.Count)
                {
                    var m = Coop.Members[i];
                    Rect(new Rect(row.x + 8, row.y + 8, 24, 24), Suits.Tint(m.suit) == Color.white ? new Color(0.9f, 0.85f, 0.7f) : Suits.Tint(m.suit));
                    Text(new Rect(row.x + 42, row.y, 190, 40), m.user.ToUpper(), label, m.host ? Gold : blue, TextAnchor.MiddleLeft, 13);
                    Text(new Rect(row.x + 230, row.y, 160, 40), (m.host ? "HOST  GOLD RINGS" : "GUEST  BLUE RINGS"), small, new Color(1, 1, 1, 0.55f), TextAnchor.MiddleRight);
                }
                else Text(new Rect(row.x + 14, row.y, lw - 20, 40), "WAITING FOR A FRIEND...", small, new Color(1, 1, 1, 0.4f), TextAnchor.MiddleLeft);
            }
            if (Coop.Role == CoopRole.Host)
            {
                GUI.enabled = Coop.Members.Count >= 2;
                if (Button(new Rect(lx, r.y + 300, lw, 56), "START CO-OP RUN")) Coop.Cmd("start");
                GUI.enabled = true;
            }
            else Text(new Rect(lx, r.y + 304, lw, 50), "WAITING FOR THE HOST TO START...", label, blue, TextAnchor.MiddleCenter, 12);
            if (Button(new Rect(lx, r.y + 366, lw, 40), "LEAVE ROOM", btnSmall)) Coop.Cmd("leave");
            Text(new Rect(lx, r.y + 412, lw, 40), "SHARE THE CODE OR INVITE A FRIEND FROM THE LIST", small, new Color(1, 1, 1, 0.4f));
        }

        // ---------------------------------------------------------------- right: friends
        Text(new Rect(rx, r.y + 92, rw, 28), "FRIENDS", label, Color.white, TextAnchor.MiddleLeft, 14);
        bool online = Coop.Conn == CoopConn.Online;
        GUI.enabled = online;
        coopFriend = Clean(Field(new Rect(rx, r.y + 124, 262, 40), coopFriend, 16, false, "FRIEND USERNAME"));
        if (Button(new Rect(rx + 272, r.y + 124, 128, 40), "ADD", btnSmall) && coopFriend.Trim().Length > 0) { Coop.Cmd("friend_add", "user", coopFriend.Trim()); coopFriend = ""; }
        GUI.enabled = true;

        int rows = Coop.Requests.Count + Coop.Friends.Count;
        var view = new Rect(rx, r.y + 176, rw, 270);
        Rect(view, new Color(0.08f, 0.07f, 0.12f, 0.9f));
        if (rows == 0)
            Text(view, online ? "NO FRIENDS YET - ADD ONE BY USERNAME" : "CONNECT TO SEE YOUR FRIENDS", small, new Color(1, 1, 1, 0.4f), TextAnchor.MiddleCenter);
        else
        {
            var content = new Rect(0, 0, rw - 18, rows * 40 + 6);
            coopScroll = GUI.BeginScrollView(view, coopScroll, content);
            int y = 3;
            foreach (string req in Coop.Requests)
            {
                Rect(new Rect(2, y, content.width - 4, 36), new Color(0.25f, 0.2f, 0.08f, 0.9f));
                Text(new Rect(10, y, 170, 36), req.ToUpper(), label, Gold, TextAnchor.MiddleLeft, 12);
                Text(new Rect(10, y + 20, 170, 16), "WANTS TO BE FRIENDS", small, new Color(1, 1, 1, 0.5f), TextAnchor.MiddleLeft);
                if (Button(new Rect(content.width - 190, y + 3, 100, 30), "ACCEPT", btnSmall)) Coop.Cmd("friend_accept", "user", req);
                if (Button(new Rect(content.width - 84, y + 3, 76, 30), "NO", btnSmall)) Coop.Cmd("friend_remove", "user", req);
                y += 40;
            }
            foreach (var f in Coop.Friends)
            {
                Rect(new Rect(2, y, content.width - 4, 36), new Color(0.12f, 0.1f, 0.18f, 0.95f));
                Rect(new Rect(10, y + 13, 10, 10), f.online ? new Color(0.3f, 1f, 0.4f) : new Color(0.4f, 0.4f, 0.45f));
                Text(new Rect(28, y, 150, 36), f.user.ToUpper(), label, f.online ? Color.white : new Color(0.6f, 0.6f, 0.65f), TextAnchor.MiddleLeft, 12);
                float bx2 = content.width - 8;
                if (Button(new Rect(bx2 - 34, y + 3, 34, 30), "X", btnSmall)) Coop.Cmd("friend_remove", "user", f.user);
                bx2 -= 40;
                if (f.online && Coop.InRoom && Coop.Role == CoopRole.Host && Coop.Members.Count < 2)
                {
                    if (Button(new Rect(bx2 - 90, y + 3, 90, 30), "INVITE", btnSmall)) Coop.Cmd("invite", "user", f.user);
                }
                else if (f.online && !Coop.InRoom && !string.IsNullOrEmpty(f.room))
                {
                    if (Button(new Rect(bx2 - 90, y + 3, 90, 30), "JOIN", btnSmall)) Coop.Cmd("join", "code", f.room);
                }
                else if (f.online && !string.IsNullOrEmpty(f.room)) Text(new Rect(bx2 - 90, y, 90, 36), "IN A ROOM", small, new Color(1, 1, 1, 0.4f), TextAnchor.MiddleRight);
                y += 40;
            }
            GUI.EndScrollView();
        }
        if (Coop.Sent.Count > 0)
            Text(new Rect(rx, r.y + 452, rw, 20), "REQUEST SENT TO: " + string.Join(", ", Coop.Sent.ToArray()).ToUpper(), small, new Color(1, 1, 1, 0.4f));

        // ---------------------------------------------------------------- footer
        if (Coop.Err.Length > 0) Text(new Rect(r.x + 20, r.y + r.height - 108, r.width - 40, 24), Coop.Err.ToUpper(), small, new Color(1f, 0.45f, 0.4f), TextAnchor.MiddleCenter);
        else if (Coop.Info.Length > 0) Text(new Rect(r.x + 20, r.y + r.height - 108, r.width - 40, 24), Coop.Info, small, new Color(0.6f, 1f, 0.6f), TextAnchor.MiddleCenter);
        if (Coop.Conn == CoopConn.Online && Button(new Rect(r.x + 30, r.y + r.height - 66, 200, 46), "DISCONNECT", btnSmall)) Coop.Disconnect();
        if (Button(new Rect(r.x + r.width / 2 - 100, r.y + r.height - 66, 200, 46), "BACK")) LeaveCoopPage();
    }
}
