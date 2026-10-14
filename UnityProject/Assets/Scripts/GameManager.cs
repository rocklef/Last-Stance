using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

// Match flow: intro, best-of-three rounds, knockouts with slow motion, victory and restart.
[DefaultExecutionOrder(-100)]
public class GameManager : MonoBehaviour
{
    public Fighter p1, p2;
    public FightCamera cam;
    public HUD hud;
    public FX fx;
    public GameObject rocPrefab;
    public GameObject monkPrefab;
    public Material[] shardMats;
    public Material warnMat;
    public GameObject guardianPrefab;   // Kaito's Guardian Spirit (a giant of blue light)
    public Material glowMat;            // his hands' blue fire during the Blade Hand Awakening
    public float roundTime = 60;

    [System.NonSerialized] public bool demo;   // both fighters AI-controlled (attract mode / auto-capture)
    public static GameManager I;

    int wins1, wins2, round;
    float timer;
    bool fighting;
    Fighter koVictim;
    bool storyMode, storyCrash, bossOnly;
    bool rocRevealed;
    StoryArena storyArena;
    public static event System.Action<string> Log;

    void Awake()
    {
        I = this;
        Application.targetFrameRate = 120;
        TimeFX.slow = 1;
        TimeFX.paused = false;
        Fighter.freeRoam = false;
        demo = AutoCapture.Active || Showcase.Active || System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-demo") >= 0;
        storyMode = false;   // one mode only: one round with Brakka, then Moros
        bossOnly = Showcase.Active || System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-boss") >= 0;
    }

    void Start()
    {
        DynamicGI.UpdateEnvironment();
        Moves.Bind(p1.GetComponent<AnimDriver>().library);
        p1.foe = p2;
        p2.foe = p1;
        if (demo)
        {
            p1.brain = p1.gameObject.AddComponent<AIBrain>();
            ((AIBrain)p1.brain).skill = 0.75f;
        }
        else p1.brain = p1.gameObject.AddComponent<PlayerBrain>();
        p2.brain = p2.gameObject.AddComponent<AIBrain>();
        ((AIBrain)p2.brain).skill = 0.55f;
        cam.a = p1;
        cam.b = p2;
        hud.Build(p1, p2, demo ? "DEMO MODE" : MobileControls.On ? "" : "ARROWS  MOVE    MOUSE  LOOK    L-CLICK  LIGHT    R-CLICK  HEAVY    R-CTRL  BLOCK / PARRY    SHIFT  DODGE    ENTER  THROW    SPACE  WALL RUN / LEAP    1-4  POWERS    MIDDLE CLICK  FIND MOROS");
        Fighter.KnockedOut += OnKO;
        SoundStage.Ensure();
        gameObject.AddComponent<PerfGovernor>();
        StartCoroutine(StartFlow());
    }

    void OnDestroy() => Fighter.KnockedOut -= OnKO;

    void Update()
    {
        TimeFX.Tick();
        var kb = Keyboard.current;
        if (((kb != null && kb.escapeKey.wasPressedThisFrame) || MobileControls.Pause) && !demo)
        {
            TimeFX.paused = !TimeFX.paused;
            if (TimeFX.paused) hud.Announce("PAUSED", MobileControls.On ? "TAP  II  TO RESUME" : "ESC  RESUME        Q  QUIT", 9999, Color.white);
            else hud.Announce("", "", 0.01f);
        }
        if (TimeFX.paused && kb != null && kb.qKey.wasPressedThisFrame) Application.Quit();
        if (kb != null && kb.f2Key.wasPressedThisFrame && !demo)
        {
            Fighter.God = !Fighter.God;
            hud.BossCall(Fighter.God ? "GOD MODE ON" : "GOD MODE OFF", Fighter.God ? "KAITO CANNOT BE HURT" : "", 1.4f, new Color(0.5f, 0.9f, 1f));
        }
        // the mouse is a controller while playing: hide and lock the cursor (freed while paused)
        if (!demo && Application.isFocused && !MobileControls.On)
        {
            var lockMode = TimeFX.paused ? CursorLockMode.None : CursorLockMode.Locked;
            if (Cursor.lockState != lockMode) { Cursor.lockState = lockMode; Cursor.visible = TimeFX.paused; }
        }
        if (fighting && !TimeFX.paused)
        {
            timer -= Time.deltaTime;
            hud.SetTimer(timer);
        }
    }

    void OnKO(Fighter victim)
    {
        if (!fighting) return;
        fighting = false;
        koVictim = victim;
    }

    IEnumerator StartFlow()
    {
        // The executable supports immediate launch with -story; otherwise both modes live behind
        // a simple controller-safe title choice.
        if (bossOnly)
        {
            p1.ResetForRound(new Vector3(-1.5f, 0, 0), Vector3.right);
            p2.ResetForRound(new Vector3(1.5f, 0, 0), Vector3.left);
            yield return MonkArrival(true);
            yield break;
        }
        if (!demo && !storyMode)
        {
            // Imported humanoids must never idle on the title card: their animation roots have not
            // been reset yet, which made them appear half-buried in the floor. The title now shows
            // the courtyard alone; fighters are activated only after a real mode is selected.
            p1.gameObject.SetActive(false);
            p2.gameObject.SetActive(false);
            hud.Announce("LAST STANCE", MobileControls.On ? "TAP TO FIGHT" : "PRESS ENTER OR CLICK TO FIGHT", 9999, new Color(1f, 0.72f, 0.25f));
            while (true)
            {
                var kb = Keyboard.current;
                var ms = Mouse.current;
                if (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame)) break;
                if (ms != null && ms.leftButton.wasPressedThisFrame) break;
                if (MobileControls.Tap) break;
                yield return null;
            }
            p1.gameObject.SetActive(true);
            p2.gameObject.SetActive(true);
            hud.Announce("", "", 0.01f);
        }
        if (storyMode) yield return StoryBattle();
        else yield return Match();
    }

    IEnumerator StoryBattle()
    {
        // One long health bar and no round timer: this is a boss encounter, not another match.
        p2.maxHealth = 235; p2.health = p2.maxHealth; p2.damageMul = 1.65f; p2.weight = 2f;
        ((AIBrain)p2.brain).skill = 0.86f;
        storyArena = gameObject.AddComponent<StoryArena>();
        storyArena.Begin(p2);
        p1.ResetForRound(new Vector3(-1.5f, 0, 0), Vector3.right);
        p2.ResetForRound(new Vector3(1.5f, 0, 0), Vector3.left);
        hud.SetWins(0, 0); hud.SetTimer(999);
        cam.SetMode(FightCamera.Mode.Intro);
        hud.Announce("TEMPLE BREAKER", "KAITO: STAND ASIDE, BRAKKA. THIS ENDS HERE.", 2.6f, p1.accent);
        yield return new WaitForSecondsRealtime(2.8f);
        hud.Announce("BRAKKA", "THEN COME AND BREAK YOURSELF ON ME.", 2.3f, new Color(1f, 0.18f, 0.08f));
        yield return new WaitForSecondsRealtime(2.5f);
        hud.Announce("SURVIVE THE STORM", "BOSS PHASE I", 1.1f, new Color(1f, 0.3f, 0.12f));
        cam.SetMode(FightCamera.Mode.Fight);
        p1.BeginFight(); p2.BeginFight(); fighting = true;
        while (p1.health > 0 && p2.health > 0)
        {
            if (!storyCrash && p2.health <= p2.maxHealth * 0.52f)
            {
                storyCrash = true;
                yield return WallCrash();
            }
            yield return null;
        }
        fighting = false;
        var winner = p1.health > 0 ? p1 : p2;
        if (winner == p1)
        {
            yield return MonkArrival(false);
            yield break;
        }
        p1.inputLocked = p2.inputLocked = true;
        TimeFX.slow = 0.25f;
        cam.SetMode(FightCamera.Mode.KO, winner == p1 ? p2 : p1);
        hud.Announce("THE TEMPLE FALLS SILENT", "KAITO HAS FALLEN", 3f, winner.accent);
        yield return new WaitForSecondsRealtime(3.2f);
        TimeFX.slow = 1;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    IEnumerator WallCrash()
    {
        fighting = false;
        p1.inputLocked = p2.inputLocked = true;
        hud.Announce("BRAKKA'S WRATH", "ONE BLOW CAN BREAK STONE.", 1.2f, new Color(1f, 0.18f, 0.08f));
        yield return new WaitForSecondsRealtime(1.25f);
        TimeFX.HitStop(0.16f);
        FightCamera.Shake(0.7f); FightCamera.Punch(1.2f);
        if (!Fighter.God) p1.health = Mathf.Max(1, p1.health - 32);
        p1.CinematicLaunch(Vector3.left, 4.2f, 12f);
        fx.Splash(p1.transform.position + Vector3.up, Vector3.left, 1f);
        yield return new WaitForSecondsRealtime(0.42f);
        storyArena.Break(Vector3.left);
        FightCamera.Shake(0.9f); FightCamera.Punch(1.4f);
        hud.Announce("WALL BREAK!", "KAITO CRASHES THROUGH THE OUTER COURTYARD", 1.5f, new Color(1f, 0.72f, 0.3f));
        yield return new WaitForSecondsRealtime(1.5f);
        p1.transform.position = new Vector3(-7.2f, 0, 0);
        p1.BeginFight(); p2.BeginFight();
        fighting = true;
        hud.Announce("BOSS PHASE II", "THE TEMPLE HAS NO MORE MERCY.", 1.1f, new Color(1f, 0.25f, 0.12f));
    }

    IEnumerator Match()
    {
        Fighter.BossInvincible = false;
        wins1 = wins2 = 0;
        round = 1;
        hud.SetWins(0, 0);
        while (true)
        {
            yield return Round();
            hud.SetWins(wins1, wins2);
            if (wins1 >= 1 || wins2 >= 1) break;   // one round with Brakka, then Moros
            round++;
        }
        yield return MatchEnd();
    }

    IEnumerator Round()
    {
        TimeFX.slow = 1;
        koVictim = null;
        p1.ResetForRound(new Vector3(-1.5f, 0, 0), Vector3.right);
        p2.ResetForRound(new Vector3(1.5f, 0, 0), Vector3.left);
        timer = roundTime;
        hud.SetTimer(timer);
        cam.SetMode(FightCamera.Mode.Intro);
        fx.DuckMusic(0.25f);
        Log?.Invoke($"round {round} intro");
        yield return new WaitForSecondsRealtime(1.2f);
        fx.Gong();
        hud.Announce(round == 1 ? "ONE ROUND" : "ROUND " + round, round == 1 ? "KAITO  VS  BRAKKA" : "", 1.5f);
        yield return new WaitForSecondsRealtime(2.1f);
        hud.Announce("FIGHT!", "", 0.7f, new Color(1f, 0.3f, 0.2f));
        cam.SetMode(FightCamera.Mode.Fight);
        fx.DuckMusic(0.42f);
        p1.BeginFight();
        p2.BeginFight();
        fighting = true;
        Log?.Invoke($"round {round} fight");

        while (fighting && timer > 0) yield return null;

        Fighter winner, loser;
        if (koVictim != null)
        {
            loser = koVictim;
            winner = loser == p1 ? p2 : p1;
            p1.inputLocked = p2.inputLocked = true;
            TimeFX.slow = 0.22f;
            cam.SetMode(FightCamera.Mode.KO, loser);
            fx.DuckMusic(0.15f);
            bool perfect = winner.health >= winner.maxHealth;
            hud.Announce(perfect ? "PERFECT K.O." : "K.O.", "", 1.8f, new Color(1f, 0.2f, 0.12f));
            Log?.Invoke($"KO {loser.displayName} by {winner.displayName} perfect={perfect}");
            yield return new WaitForSecondsRealtime(1.9f);
            TimeFX.slow = 1f;
            float wait = 0;
            while (loser.state != Fighter.State.KO && wait < 3) { wait += Clock.Dt; yield return null; }
        }
        else
        {
            fighting = false;
            p1.inputLocked = p2.inputLocked = true;
            float r1 = p1.health / p1.maxHealth, r2 = p2.health / p2.maxHealth;
            winner = r1 >= r2 ? p1 : p2;
            loser = winner == p1 ? p2 : p1;
            if (Mathf.Approximately(r1, r2)) winner = null;
            hud.Announce("TIME", winner ? winner.displayName.ToUpper() + " WINS ON HEALTH" : "DRAW", 1.6f, Color.white);
            Log?.Invoke($"time over winner={(winner ? winner.displayName : "draw")}");
            yield return new WaitForSecondsRealtime(1.8f);
        }

        if (winner)
        {
            if (winner == p1) wins1++; else wins2++;
            yield return new WaitForSecondsRealtime(0.3f);
            winner.Celebrate();
            cam.SetMode(FightCamera.Mode.Victory, winner);
            fx.DuckMusic(0.3f);
            hud.Announce(winner.displayName.ToUpper() + " WINS", "", 1.8f, winner.accent);
            yield return new WaitForSecondsRealtime(2.6f);
        }
    }

    IEnumerator MatchEnd()
    {
        var champ = wins1 >= 1 ? p1 : p2;
        // The Roc is a reward/escalation after a genuine player victory over Brakka. It never
        // appears during AI capture runs or if Brakka wins, so the original match remains intact.
        if (!rocRevealed && (champ == p1 || demo))
        {
            rocRevealed = true;
            yield return MonkArrival(false);
            yield break;
        }
        string sub = demo ? "" : MobileControls.On ? "TAP TO FIGHT AGAIN" : "PRESS ENTER TO FIGHT AGAIN";
        string big = !demo && champ == p2 ? "DEFEATED" : champ.displayName.ToUpper() + " IS VICTORIOUS";
        hud.Announce(big, sub, 9999, champ == p1 ? new Color(1f, 0.85f, 0.3f) : new Color(1f, 0.25f, 0.15f));
        Log?.Invoke($"match over champion={champ.displayName} {wins1}-{wins2}");
        float t = 0;
        while (true)
        {
            t += Clock.Dt;
            var kb = Keyboard.current;
            var pad = Gamepad.current;
            bool go = (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)) ||
                      (pad != null && pad.startButton.wasPressedThisFrame) || (MobileControls.Tap && t > 1f);
            if (go || (demo && t > 4f && !AutoCapture.Active)) break;
            yield return null;
        }
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    // MOROS, THE SHARD MONK: the story boss after Brakka (replaces the Ashen Roc). Brakka's fighter
    // stays behind as an invisible anchor that follows Moros, so Kaito faces and spaces against him
    // and the right-hand health bar shows the monk's health.
    IEnumerator MonkArrival(bool skipIntro)
    {
        Fighter.BossInvincible = true;   // the final battle: only Moros's health goes down
        p1.inputLocked = p2.inputLocked = true;
        if (!skipIntro)
        {
            cam.SetMode(FightCamera.Mode.Victory, p1);
            hud.Announce("BRAKKA FALLS", "THE COURTYARD FALLS SILENT.", 2f, p1.accent);
            yield return new WaitForSecondsRealtime(2.15f);
            hud.Announce("FINAL BATTLE", "MOROS, THE SHARD MONK AWAKENS", 2.2f, new Color(1f, 0.3f, 0.12f));
            yield return new WaitForSecondsRealtime(2.3f);
        }
        TimeFX.slow = 1;
        // Kaito comes into the boss fight fresh, wherever he stood
        Vector3 kp = p1.transform.position; kp.y = 0;
        p1.ResetForRound(kp, p1.transform.forward.sqrMagnitude > 0.01f ? new Vector3(p1.transform.forward.x, 0, p1.transform.forward.z) : Vector3.forward);
        var morosColor = new Color(1f, 0.3f, 0.12f);
        p2.ResetForRound(new Vector3(0, 0, 4.2f), Vector3.back);
        p2.displayName = "Moros";
        p2.accent = morosColor;
        hud.Rebrand(1, "MOROS", "THE SHARD MONK", morosColor);
        hud.SetWins(0, 0); hud.SetTimer(999);
        p2.gameObject.SetActive(false);
        Log?.Invoke("boss moros begins");
        var boss = new GameObject("MorosEncounter").AddComponent<MonkBoss>();
        boss.Begin(p1, p2, monkPrefab, shardMats, warnMat);
        while (!boss.Complete) yield return null;
        p1.inputLocked = true;
        if (boss.HeroWon)
        {
            Log?.Invoke($"match over champion=Kaito boss=Moros kaitoHp={p1.health}");
            p1.Celebrate();
            cam.SetMode(FightCamera.Mode.Victory, p1);
            hud.Announce("KAITO IS VICTORIOUS", demo ? "" : "PRESS ENTER TO FIGHT AGAIN", 9999, new Color(1f, 0.85f, 0.3f));
        }
        else
        {
            Log?.Invoke("match over champion=Moros");
            TimeFX.slow = 0.3f;
            cam.SetMode(FightCamera.Mode.KO, p1);
            hud.Announce("THE SHARDS CLAIM KAITO", demo ? "" : MobileControls.On ? "TAP TO TRY AGAIN" : "PRESS ENTER TO TRY AGAIN", 9999, new Color(1f, 0.25f, 0.15f));
            yield return new WaitForSecondsRealtime(2f);
            TimeFX.slow = 1;
        }
        while (true)
        {
            var kb = Keyboard.current;
            var pad = Gamepad.current;
            if ((kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)) || (pad != null && pad.startButton.wasPressedThisFrame) || MobileControls.Tap) break;
            yield return null;
        }
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    IEnumerator RocArrival()
    {
        p1.inputLocked = p2.inputLocked = true;
        cam.SetMode(FightCamera.Mode.Victory, p1);
        hud.Announce("BRAKKA FALLS", "THE COURTYARD FALLS SILENT.", 2f, p1.accent);
        yield return new WaitForSecondsRealtime(2.15f);
        p2.gameObject.SetActive(false);
        var rocObject = new GameObject("AshenRocEncounter");
        var roc = rocObject.AddComponent<SegmentedRoc>();
        roc.Begin(p1, rocPrefab);
        yield return new WaitForSecondsRealtime(3.6f);
        p1.BeginFight();
        hud.Announce("BOSS REVEAL", "THE ASHEN ROC DESCENDS", 1.8f, new Color(1f, .36f, .12f));
        while (!roc.Complete) yield return null;
        p1.inputLocked = true;
        hud.Announce("TO BE CONTINUED", "THE ROC HAS MARKED KAITO.", 9999, new Color(1f, .36f, .12f));
    }
}
