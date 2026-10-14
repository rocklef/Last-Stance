using System.Collections;
using UnityEngine;

// MOROS, part three: the showcase. In showcase mode (see Showcase.cs) the fight loop never starts;
// the trailer's director calls his powers one by one - "axe", "hunt", "gale", "dragon", "titan"... -
// places him for each scene, and sets his health so the story reads right.
public partial class MonkBoss
{
    public static bool ShowcaseMode;
    bool showBusy = true;

    public bool ShowBusy => showBusy;
    public string Act => act;
    public Vector3 Pos => pos;
    public float MaxHp => maxHp;
    public float Hp { get => hp; set => hp = Mathf.Clamp(value, 0, maxHp); }
    public Vector3 WeaponPos => WeaponPoint();
    public Vector3 HeadPoint => form == Form.Titan ? ShardForms.TitanHead(tp) : form == Form.GreatDragon ? drag[0] : pos + Vector3.up * 1.9f;

    public void Perform(string name)
    {
        if (Ended) return;
        if (AutoCapture.Active) Debug.Log($"[AUTO] perform {name} at {Time.timeSinceLevelLoad:0.0}s");
        StopAllCoroutines();
        showBusy = true;
        StartCoroutine(PerformRoutine(name));
    }

    IEnumerator PerformRoutine(string name)
    {
        switch (name)
        {
            case "axe": yield return Axe(); break;
            case "grasp": yield return Grasp(); break;
            case "lances": yield return Lances(); break;
            case "serpent": yield return Serpent(); break;
            case "roc": yield return Roc(); break;
            case "hammer": yield return Hammer(); break;
            case "flood": yield return Flood(); break;
            case "gatling": yield return Gatling(); break;
            case "rockets": yield return Rockets(); break;
            case "dragon": yield return GreatDragon(); break;
            case "sphere": yield return RollingSphere(); break;
            case "cobra": yield return CobraStrike(); break;
            case "drill": yield return DrillAttack(); break;
            case "wall": yield return WallAttack(); break;
            case "arsenal": yield return Arsenal(); break;
            case "gale": yield return Gale(); break;
            case "seekers": yield return Seekers(); break;
            case "hunt": yield return Hunt(); break;
            case "brawl": yield return Brawl(); break;
            case "hover": yield return Hover(1.5f); break;
            case "titan": yield return AscensionShow(); yield break;
            case "titan_slam": yield return TitanHammerFist(); break;
            case "titan_stomp": yield return TitanStompAttack(); break;
            case "titan_grab": yield return TitanRunGrab(); break;
            case "titan_leap": yield return TitanLeapSmash(); break;
            case "titan_sweep": yield return TitanSweep(); break;
            case "titan_chase": yield return TitanChase(3f); break;
        }
        showBusy = false;
        if (form == Form.Titan) while (true) { TitanMove(0, HeroFlat, 99f); yield return null; }   // the titan keeps standing, watching him
        swarm.mode = ShardSwarm.Mode.Halo;
        while (true) { goal = HoverSpot(4.5f); height = 1.5f; yield return null; }   // idle between scenes
    }

    public void SetPhase(bool isArmed, bool isAscended, bool isEnraged)
    {
        armed = isArmed;
        ascended = isAscended;
        enraged = isEnraged;
    }

    // cut: put him somewhere new, his swarm gathered around him
    public void Place(Vector3 at, float h)
    {
        StopAllCoroutines();
        EndForms();
        showBusy = false;
        act = "hover";
        pos = new Vector3(at.x, h, at.z);
        goal = Flat(pos);
        height = h;
        posVel = Vector3.zero;
        heightVel = 0;
        if (model) model.transform.position = pos;
        swarm.center = pos;
        swarm.Gather(pos + Vector3.up * 1.2f, 2.5f);
        swarm.mode = ShardSwarm.Mode.Halo;
        StartCoroutine(IdleHover());
    }

    IEnumerator IdleHover()
    {
        Play("Idle", 0.2f);
        while (true) { goal = HoverSpot(4.5f); height = 1.5f; yield return null; }
    }

    // make the nearest searching swarm spot Kaito
    public void ShowcaseSpot()
    {
        Hunter best = null;
        float bd = float.MaxValue;
        foreach (var h in hunters)
        {
            if (h == null || (h.state != Hunter.State.Roam && h.state != Hunter.State.Search)) continue;
            float d = (h.Head - HeroChest).sqrMagnitude;
            if (d < bd) { bd = d; best = h; }
        }
        best?.ForceChase();
    }

    public bool AnyHunterNear(float r)
    {
        foreach (var h in hunters)
            if (h != null && h.state != Hunter.State.Broken && (h.Head - HeroChest).magnitude < r) return true;
        return false;
    }

    // THE FINALE: the transformation in full - a tornado, then a titan that builds itself from the boots
    // up while it walks after Kaito - its roar, a first slam, and then it stands waiting
    IEnumerator AscensionShow()
    {
        ascended = true;
        enraged = true;
        invulnerable = true;
        titanCracks = 0;
        act = "ascension";
        Cleanup();
        swarm.active = swarm.Count;
        GameManager.I.hud.Rebrand(1, "MOROS ASCENDED", "THE LIVING TEMPLE", Ember);
        Play("PowerUp", 0.15f);
        swarm.mode = ShardSwarm.Mode.Twister;
        swarm.center = Flat(pos);
        swarm.twisterHeight = 12f; swarm.twisterWidth = 0.5f;
        float nextShake = 0;
        for (float x = 0; x < 3.2f; x += Time.deltaTime)
        {
            goal = Flat(pos);
            height = Mathf.Lerp(1.5f, 6.3f, x / 3.2f);
            if (x > nextShake) { FightCamera.Shake(0.12f); nextShake += 0.6f; }
            yield return null;
        }
        // it builds itself from the feet up while it strides after Kaito
        TitanBegin();
        form = Form.Titan;
        swarm.chipScale = 2.6f;
        swarm.formStretch = 2.6f;
        swarm.reservoirStorm = true;
        swarm.stormCenter = tRoot;
        const float build = 7f;
        swarm.BeginForm(build, true);
        for (float x = 0; x < build + 0.6f; x += Time.deltaTime)
        {
            TitanMove(x > 1.6f ? TitanWalk : 0, HeroFlat, 7f);
            swarm.stormCenter = tRoot;
            swarm.stormBase = Mathf.Lerp(0.5f, 9f, x / build);
            if (x > build * 0.75f && model.activeSelf) model.SetActive(false);
            yield return null;
        }
        swarm.reservoirStorm = false;
        model.SetActive(false);
        swarm.twisterHeight = 4.6f; swarm.twisterWidth = 0.32f;
        GameManager.I.fx.Impact(tRoot, 9f, 0.38f);
        GameManager.I.fx.Chroma(1f);
        FightCamera.Shake(0.8f);
        FightCamera.Punch(1f);
        Announce("MOROS ASCENDED", "I AM THE TEMPLE.", 2.4f);
        tArmBusy[0] = tArmBusy[1] = true;
        tLeanWant = -0.35f;
        for (float x = 0; x < 1.6f; x += Time.deltaTime)
        {
            TitanMove(0, HeroFlat, 99f);
            Vector3 f = TitanForward, r = Right(f);
            for (int sd = -1; sd <= 1; sd += 2)
                SetTitanHand(sd, ShardForms.TitanShoulder(tp, sd) + (r * sd * 3.4f + Vector3.up * 1.6f + f * 0.6f) * TitanScale, (r * sd + Vector3.up).normalized, f, 0.2f, 1 - Mathf.Exp(-6 * Time.deltaTime));
            yield return null;
        }
        TitanRelax();
        showBusy = false;
        while (true) { TitanMove(0, HeroFlat, 99f); yield return null; }
    }
}
