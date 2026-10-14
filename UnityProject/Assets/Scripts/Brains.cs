using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// Keyboard / gamepad control. Movement is screen-relative: right on the stick moves right on screen.
public class PlayerBrain : Brain
{
    float ctrlYaw = float.NaN;

    public override Intent Think(Fighter self, Fighter foe, float dt)
    {
        var i = new Intent();
        Vector2 m = Vector2.zero;
        var kb = Keyboard.current;
        var pad = Gamepad.current;
        bool light = false, heavy = false;
        if (kb != null)
        {
            // arrow keys move (WASD still works); the keys right beside the arrows cover the rest
            if (kb.leftArrowKey.isPressed || kb.aKey.isPressed) m.x -= 1;
            if (kb.rightArrowKey.isPressed || kb.dKey.isPressed) m.x += 1;
            if (kb.upArrowKey.isPressed || kb.wKey.isPressed) m.y += 1;
            if (kb.downArrowKey.isPressed || kb.sKey.isPressed) m.y -= 1;
            light |= kb.jKey.wasPressedThisFrame || kb.zKey.wasPressedThisFrame;
            heavy |= kb.kKey.wasPressedThisFrame || kb.xKey.wasPressedThisFrame;
            i.dash |= kb.leftShiftKey.wasPressedThisFrame || kb.rightShiftKey.wasPressedThisFrame;
            i.jump |= kb.spaceKey.wasPressedThisFrame;
            // hero powers: number row, numpad, or the Delete / End / Page Down / Insert block above the arrows
            if (kb.digit1Key.wasPressedThisFrame || kb.numpad1Key.wasPressedThisFrame || kb.deleteKey.wasPressedThisFrame) i.power = 1;
            if (kb.digit2Key.wasPressedThisFrame || kb.numpad2Key.wasPressedThisFrame || kb.endKey.wasPressedThisFrame) i.power = 2;
            if (kb.digit3Key.wasPressedThisFrame || kb.numpad3Key.wasPressedThisFrame || kb.pageDownKey.wasPressedThisFrame) i.power = 3;
            if (kb.digit4Key.wasPressedThisFrame || kb.numpad4Key.wasPressedThisFrame || kb.insertKey.wasPressedThisFrame || kb.gKey.wasPressedThisFrame) i.power = 4;
            i.block |= kb.lKey.isPressed || kb.cKey.isPressed || kb.rightCtrlKey.isPressed || kb.numpad0Key.isPressed;
            i.grab |= kb.uKey.wasPressedThisFrame || kb.vKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame;
        }
        // mouse: left = light, right = heavy, both = throw; side buttons = block (back) / dash (forward)
        var mouse = Mouse.current;
        if (mouse != null && Cursor.lockState == CursorLockMode.Locked)
        {
            light |= mouse.leftButton.wasPressedThisFrame;
            heavy |= mouse.rightButton.wasPressedThisFrame;
            if (mouse.leftButton.wasPressedThisFrame && mouse.rightButton.isPressed) light = heavy = true;
            if (mouse.rightButton.wasPressedThisFrame && mouse.leftButton.isPressed) light = heavy = true;
            i.block |= mouse.backButton.isPressed;
            i.dash |= mouse.forwardButton.wasPressedThisFrame;
        }
        if (pad != null)
        {
            var s = pad.leftStick.ReadValue() + pad.dpad.ReadValue();
            if (s.sqrMagnitude > 0.05f) m = s;
            bool mod = pad.rightTrigger.ReadValue() > 0.5f;
            light |= !mod && pad.buttonWest.wasPressedThisFrame;
            heavy |= !mod && pad.buttonNorth.wasPressedThisFrame;
            bool powerMod = pad.rightTrigger.ReadValue() > 0.5f;
            i.dash |= pad.rightShoulder.wasPressedThisFrame;
            i.jump |= !powerMod && pad.buttonSouth.wasPressedThisFrame;
            if (powerMod)
            {
                if (pad.buttonWest.wasPressedThisFrame) i.power = 1;
                if (pad.buttonNorth.wasPressedThisFrame) i.power = 2;
                if (pad.buttonEast.wasPressedThisFrame) i.power = 3;
                if (pad.buttonSouth.wasPressedThisFrame) i.power = 4;
            }
            i.block |= pad.leftShoulder.isPressed || pad.leftTrigger.ReadValue() > 0.4f;
            i.grab |= !mod && pad.buttonEast.wasPressedThisFrame;
        }
        // phone: the on-screen stick and buttons
        if (MobileControls.On)
        {
            if (MobileControls.Move.sqrMagnitude > 0.01f) m = MobileControls.Move;
            light |= MobileControls.Light;
            heavy |= MobileControls.Heavy;
            i.dash |= MobileControls.Dash;
            i.jump |= MobileControls.Jump;
            i.grab |= MobileControls.Grab;
            i.block |= MobileControls.Block;
            if (MobileControls.Power != 0) i.power = MobileControls.Power;
        }
        // light + heavy together is also a throw
        if (light && heavy) i.grab = true;
        else { i.light = light; i.heavy = heavy; }

        // screen-relative movement; when the camera cuts to a new angle the old mapping is kept
        // until the stick is released (then eases over), so a cut never reverses your run
        var cam = Camera.main.transform;
        float camYaw = cam.eulerAngles.y;
        if (float.IsNaN(ctrlYaw) || m.sqrMagnitude < 0.04f || BossDirector.Manual) ctrlYaw = camYaw;   // mouse look: always camera-relative
        else ctrlYaw = Mathf.MoveTowardsAngle(ctrlYaw, camYaw, 60f * dt);
        var q = Quaternion.Euler(0, ctrlYaw, 0);
        i.move = Vector3.ClampMagnitude(q * Vector3.right * m.x + q * Vector3.forward * m.y, 1);
        return i;
    }
}

// Opponent AI: closes distance, circles, throws strings; reads incoming attacks and chooses to
// block, parry or sidestep them; punishes whiffs and parries, throws turtles, breaks throws,
// and backs off to recover stamina.
public class AIBrain : Brain
{
    [Range(0, 1)] public float skill = 0.6f;

    enum Plan { Approach, Hover, Attack, Retreat, Circle }
    Plan plan = Plan.Approach;
    float planT, planDur;
    readonly Queue<Btn> str = new Queue<Btn>();
    float circleSign = 1;
    float react;

    // defence against the foe's current attack
    enum Defence { None, Block, Parry, Dodge, Take }
    int readMoveId = -1;
    Defence defence;
    float defenceHold, defenceDelay, turtleT;
    float foeBlockT;
    int grabbedSeenFrame = -1;
    bool breakDecided;

    static readonly Btn[][] Strings =
    {
        new[] { Btn.Light, Btn.Light },
        new[] { Btn.Light, Btn.Light, Btn.Light },
        new[] { Btn.Light, Btn.Light, Btn.Light, Btn.Light, Btn.Light },
        new[] { Btn.Light, Btn.Light, Btn.Heavy },
        new[] { Btn.Light, Btn.Heavy, Btn.Heavy },
        new[] { Btn.Heavy },
        new[] { Btn.Heavy, Btn.Heavy },
        new[] { Btn.Heavy, Btn.Light, Btn.Light, Btn.Heavy },
        new[] { Btn.Light, Btn.Light, Btn.Light, Btn.Heavy },
    };

    void Pick(Plan p, float dur) { plan = p; planT = 0; planDur = dur; }

    public override Intent Think(Fighter self, Fighter foe, float dt)
    {
        var i = new Intent();
        planT += dt;
        react -= dt;
        defenceHold -= dt;
        defenceDelay -= dt;
        turtleT -= dt;
        Vector3 to = foe.transform.position - self.transform.position;
        to.y = 0;
        float dist = to.magnitude;
        Vector3 dir = dist > 0.01f ? to / dist : self.transform.forward;
        Vector3 side = Vector3.Cross(Vector3.up, dir) * circleSign;
        float engage = 0.95f;

        // ---- being thrown: try to break
        if (self.state == Fighter.State.Thrown)
        {
            if (!breakDecided)
            {
                breakDecided = true;
                grabbedSeenFrame = Random.value < skill * 0.55f ? Time.frameCount + Random.Range(4, 14) : -1;
            }
            if (Time.frameCount == grabbedSeenFrame) i.grab = true;
            return i;
        }
        breakDecided = false;

        // ---- feed the current string while attacking
        if (self.state == Fighter.State.Attack)
        {
            if (str.Count > 0 && react <= 0)
            {
                var b = str.Dequeue();
                if (b == Btn.Light) i.light = true; else i.heavy = true;
                react = Random.Range(0.08f, 0.16f);
            }
            return i;
        }
        if (!self.CanAct) { str.Clear(); return i; }

        // never pile on a fighter who is down or getting up: step back and wait in guard
        if (foe.IsDown || foe.state == Fighter.State.GetUp)
        {
            str.Clear();
            if (plan == Plan.Attack) Pick(Plan.Hover, 0.5f);
            i.move = (dist < 1.7f ? -dir : dist > 2.3f ? dir : Vector3.zero) * 0.7f + side * 0.3f;
            return i;
        }

        // ---- read the foe's attack once per move and pick a defence
        if (foe.state == Fighter.State.Attack && foe.move != null && foe.moveId != readMoveId && dist < foe.move.reach + 0.6f)
        {
            readMoveId = foe.moveId;
            float r = Random.value;
            float parry = 0.08f + skill * 0.22f, block = 0.25f + skill * 0.35f, dodge = 0.08f + skill * 0.12f;
            if (self.stamina < 25) { block *= 0.5f; dodge = 0; }
            defence = r < parry ? Defence.Parry : r < parry + block ? Defence.Block : r < parry + block + dodge ? Defence.Dodge : Defence.Take;
            bool alreadyGuarding = self.Blocking;
            if (alreadyGuarding) defence = Random.value < 0.85f ? Defence.Block : Defence.Take;   // keep the guard up through a string
            defenceHold = foe.move.duration / Mathf.Max(0.5f, foe.attackSpeed * foe.move.speed) + 0.1f;
            defenceDelay = alreadyGuarding ? 0 : Mathf.Lerp(0.24f, 0.11f, skill) + Random.Range(0f, 0.06f);   // human-ish reaction time
            if (defence == Defence.Dodge && self.stamina >= 20)
            {
                i.dash = true;
                i.move = side;
                return i;
            }
        }
        if (foe.state != Fighter.State.Attack && foe.state != Fighter.State.Throwing && defenceHold < 0) defence = Defence.None;

        if (turtleT > 0 && self.stamina > 20)
        {
            i.block = true;
            if (dist > 1.3f) i.move = dir * 0.5f;
            return i;
        }
        if (defence == Defence.Block && defenceHold > 0 && defenceDelay <= 0)
        {
            i.block = true;
            return i;
        }
        if (defence == Defence.Parry && foe.state == Fighter.State.Attack && foe.move != null)
        {
            // press guard right before the strike arrives
            float until = (foe.move.activeStart - foe.MoveTime) / Mathf.Max(0.5f, foe.attackSpeed * foe.move.speed);
            i.block = until < 0.09f && defenceDelay <= 0.1f;
            if (i.block) return i;
        }

        // ---- punish: foe staggered or whiffed a big move
        bool punishable = foe.state == Fighter.State.Parried || (foe.state == Fighter.State.Hitstun && foe.Exhausted) ||
                          (foe.state == Fighter.State.Attack && foe.move != null && foe.MoveTime > foe.move.activeEnd + 0.08f && foe.move.heavy);
        if (punishable && dist < engage + 0.35f)
        {
            if (Random.value < 0.4f + skill * 0.5f)
            {
                StartString(new[] { Btn.Light, Btn.Light, Btn.Light, Btn.Light }, ref i);
                return i;
            }
        }

        // ---- throw a turtling foe
        foeBlockT = foe.Blocking ? foeBlockT + dt : 0;
        if (foeBlockT > 0.45f && dist < 1.12f && Random.value < 0.03f + skill * 0.05f)
        {
            i.grab = true;
            foeBlockT = 0;
            Pick(Plan.Hover, 0.6f);
            return i;
        }

        if (foe.IsDown && plan == Plan.Attack) Pick(Plan.Hover, 0.6f);
        if (self.stamina < 20 && plan != Plan.Retreat) Pick(Plan.Retreat, 0.9f);

        switch (plan)
        {
            case Plan.Approach:
                i.move = dir + side * 0.25f;
                if (dist > 4.5f && self.stamina > 50 && Random.value < 0.02f) i.dash = true;
                if (dist < engage + 0.15f) Pick(Random.value < 0.35f + skill * 0.4f ? Plan.Attack : Plan.Hover, Random.Range(0.3f, 0.8f));
                break;
            case Plan.Hover:
                i.move = (dist > engage + 0.4f ? dir : dist < engage - 0.2f ? -dir : Vector3.zero) * 0.8f + side * 0.4f;
                if (planT > planDur)
                {
                    float r = Random.value;
                    if (foe.state == Fighter.State.Attack && dist < 1.6f && r < 0.4f) Pick(Plan.Retreat, 0.4f);
                    else if (r < 0.35f && dist < 1.5f) turtleT = Random.Range(0.4f, 1.0f);
                    else if (r < 0.55f + skill * 0.3f) Pick(Plan.Attack, 1f);
                    else if (r < 0.8f) { circleSign = -circleSign; Pick(Plan.Circle, Random.Range(0.5f, 1.1f)); }
                    else Pick(Plan.Retreat, Random.Range(0.4f, 0.8f));
                }
                break;
            case Plan.Circle:
                i.move = side + (dist > engage + 0.6f ? dir * 0.5f : Vector3.zero);
                if (Random.value < 0.01f && self.stamina > 40) i.dash = true;
                if (planT > planDur) Pick(dist > engage + 0.4f ? Plan.Approach : Plan.Attack, 1f);
                break;
            case Plan.Retreat:
                i.move = -dir + side * 0.3f;
                i.block = dist < 1.4f && self.stamina > 15 && Random.value < 0.5f;
                if (planT < dt * 1.5f && Random.value < 0.3f && self.stamina > 30) i.dash = true;
                if (planT > planDur && self.stamina > 35) Pick(Plan.Approach, 1f);
                break;
            case Plan.Attack:
                if (dist > engage + 0.25f) { i.move = dir; if (planT > 1.2f) Pick(Plan.Approach, 1f); break; }
                if (Random.value < 0.14f && dist < 1.12f) { i.grab = true; Pick(Plan.Hover, 0.6f); break; }
                if (Random.value < 0.18f)
                {
                    // launcher into a juggle
                    i.move = dir;
                    i.heavy = true;
                    str.Clear();
                    for (int k = 0; k < 3; k++) str.Enqueue(Btn.Light);
                    react = 0.3f;
                    Pick(Plan.Hover, 0.8f);
                    break;
                }
                StartString(Strings[Random.Range(0, Strings.Length)], ref i);
                Pick(Random.value < 0.5f ? Plan.Hover : Plan.Retreat, Random.Range(0.4f, 1.0f - skill * 0.5f));
                break;
        }
        i.move = Vector3.ClampMagnitude(i.move, 1);
        return i;
    }

    void StartString(Btn[] s, ref Intent i)
    {
        str.Clear();
        for (int k = 1; k < s.Length; k++) if (Random.value < 0.55f + skill * 0.45f) str.Enqueue(s[k]); else break;
        if (s[0] == Btn.Light) i.light = true; else i.heavy = true;
        react = Random.Range(0.1f, 0.2f);
    }
}
