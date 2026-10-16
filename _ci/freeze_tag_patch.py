from pathlib import Path
import sys

root=Path(sys.argv[1]).resolve()

def edit(rel, old, new, count=1):
    p=root/rel
    s=p.read_text(encoding='utf-8')
    n=s.count(old)
    if n < count:
        raise SystemExit(f'patch miss {rel}: wanted {count} occurrences, found {n}: {old[:120]!r}')
    s=s.replace(old,new,count)
    p.write_text(s,encoding='utf-8')
    print('patched',rel)

def write(rel, content):
    p=root/rel
    p.parent.mkdir(parents=True,exist_ok=True)
    p.write_text(content,encoding='utf-8')
    print('wrote',rel)

edit('src/MphRead/Formats/Formats.cs',
'''        Headhunter = 22,
        OneInTheChamber = 23
    }''',
'''        Headhunter = 22,
        OneInTheChamber = 23,
        FreezeTag = 27
    }''')

edit('src/MphRead/Mods/Network/MatchTypeCatalog.cs',
'''            new("One in the Chamber", GameMode.OneInTheChamber, GameMode.OneInTheChamber, FfaOnly: true),
            new("Kill Confirmed", GameMode.KillConfirmed, GameMode.KillConfirmedTeams),''',
'''            new("One in the Chamber", GameMode.OneInTheChamber, GameMode.OneInTheChamber, FfaOnly: true),
            new("Freeze Tag", GameMode.FreezeTag, GameMode.FreezeTag, TeamOnly: true),
            new("Kill Confirmed", GameMode.KillConfirmed, GameMode.KillConfirmedTeams),''')

edit('src/MphRead/GameState.cs',
'''                || mode == GameMode.Capture || mode == GameMode.BountyTeams || mode == GameMode.HardpointTeams || mode == GameMode.KillConfirmedTeams
                || mode == GameMode.NodesTeams || mode == GameMode.DefenderTeams;''',
'''                || mode == GameMode.Capture || mode == GameMode.FreezeTag || mode == GameMode.BountyTeams || mode == GameMode.HardpointTeams || mode == GameMode.KillConfirmedTeams
                || mode == GameMode.NodesTeams || mode == GameMode.DefenderTeams;''')
edit('src/MphRead/GameState.cs',
'''            else if (Mode == GameMode.Capture)
            {
                if (applyDefaults) { PointGoal = 5; }
                if (applyDefaults) { MatchTime = 15 * 60; }
                ModeState = ModeStateCapture;
            }''',
'''            else if (Mode == GameMode.Capture)
            {
                if (applyDefaults) { PointGoal = 5; }
                if (applyDefaults) { MatchTime = 15 * 60; }
                ModeState = ModeStateCapture;
            }
            else if (Mode == GameMode.FreezeTag)
            {
                if (applyDefaults) { PointGoal = Mods.FreezeTag.FreezeTagRules.RoundWinLimit; }
                if (applyDefaults) { MatchTime = Mods.FreezeTag.FreezeTagRules.DefaultRoundSeconds; }
                ModeState = Mods.FreezeTag.FreezeTagRuntime.Tick;
            }''')
edit('src/MphRead/GameState.cs',
'''            if (Mode == GameMode.Battle || Mode == GameMode.BattleTeams || Mode == GameMode.GunGame || IsTokenMode || Mode == GameMode.Capture || Mode == GameMode.Bounty
                || Mode == GameMode.BountyTeams || Mode == GameMode.Nodes || Mode == GameMode.NodesTeams)''',
'''            if (Mode == GameMode.Battle || Mode == GameMode.BattleTeams || Mode == GameMode.GunGame || IsTokenMode || Mode == GameMode.Capture || Mode == GameMode.FreezeTag || Mode == GameMode.Bounty
                || Mode == GameMode.BountyTeams || Mode == GameMode.Nodes || Mode == GameMode.NodesTeams)''',1)

edit('src/MphRead/Mods/Network/MatchDefinition.cs',
'''            GameMode.Capture => 5,
            GameMode.KillConfirmed or GameMode.KillConfirmedTeams or GameMode.Headhunter => 25,''',
'''            GameMode.Capture => 5,
            GameMode.FreezeTag => 5,
            GameMode.KillConfirmed or GameMode.KillConfirmedTeams or GameMode.Headhunter => 25,''')

edit('src/MphRead/SceneSetup.cs',
'''        internal static int GetMultiplayerEntityLayer(GameMode mode, int playerCount,
            Mods.Multiplayer.ResourceSpawnProfile resources)
        {
            if (mode == GameMode.BattleTeams''',
'''        internal static int GetMultiplayerEntityLayer(GameMode mode, int playerCount,
            Mods.Multiplayer.ResourceSpawnProfile resources)
        {
            if (mode == GameMode.FreezeTag) mode = GameMode.BattleTeams;
            if (mode == GameMode.BattleTeams''')

edit('src/MphRead/Mods/Multiplayer/MapModeCapabilities.cs',
'''        if (mode == GameMode.InstaGib) mode = GameMode.Battle;''',
'''        if (mode == GameMode.InstaGib) mode = GameMode.Battle;
        if (mode == GameMode.FreezeTag) mode = GameMode.BattleTeams;''',2)

edit('src/MphRead/Entities/Players/PlayerEntity.cs',
'''            if (Mods.Network.NetDamage.Suppress(this, source, flags))
            {
                return;
            }
            if (_health == 0)''',
'''            if (Mods.Network.NetDamage.Suppress(this, source, flags))
            {
                return;
            }
            if (Mods.FreezeTag.FreezeTagRules.Active(_scene.GameState.Mode) && ModFreezeTagFrozen)
            {
                if (flags.TestFlag(DamageFlags.Death)) ModFreezeTagHazardRelease();
                else if (direction.HasValue) ModFreezeTagApplyKnockback(direction.Value);
                return;
            }
            if (_health == 0)''')
edit('src/MphRead/Entities/Players/PlayerEntity.cs',
'''            if (damage > 0 || dead)
                ModControllerFeedback(dead ? Mods.Input.GamepadFeedback.Death
                    : bomb != null || beam?.Beam == BeamType.Missile || beam?.Beam == BeamType.Magmaul ? Mods.Input.GamepadFeedback.Explosion
                    : Mods.Input.GamepadFeedback.Damage);
            if (dead)''',
'''            if (damage > 0 || dead)
                ModControllerFeedback(dead ? Mods.Input.GamepadFeedback.Death
                    : bomb != null || beam?.Beam == BeamType.Missile || beam?.Beam == BeamType.Magmaul ? Mods.Input.GamepadFeedback.Explosion
                    : Mods.Input.GamepadFeedback.Damage);
            if (dead && Mods.FreezeTag.FreezeTagRules.Active(_scene.GameState.Mode))
            {
                ModFreezeTagFreeze(attacker);
                return;
            }
            if (dead)''')

edit('src/MphRead/Mods/Render/PlayerEntityTeamScoreboard.cs',
'''            bool timed = _scene.GameState.Mode == GameMode.SurvivalTeams || _scene.GameState.Mode == GameMode.DefenderTeams;
            bool deaths = _scene.GameState.Mode == GameMode.BattleTeams || _scene.GameState.Mode == GameMode.SurvivalTeams;''',
'''            bool freezeTag = _scene.GameState.Mode == GameMode.FreezeTag;
            bool timed = _scene.GameState.Mode == GameMode.SurvivalTeams || _scene.GameState.Mode == GameMode.DefenderTeams;
            bool deaths = _scene.GameState.Mode == GameMode.BattleTeams || _scene.GameState.Mode == GameMode.SurvivalTeams;''')
edit('src/MphRead/Mods/Render/PlayerEntityTeamScoreboard.cs',
'''            DrawText2D(ModScoreColumn1, y, Align.Center, 0, timed ? "TIME" : "POINTS", ink);
            DrawText2D(ModScoreColumn2, y, Align.Center, 0, deaths ? "DEATHS" : "KILLS", ink);''',
'''            DrawText2D(ModScoreColumn1, y, Align.Center, 0, freezeTag ? "FREEZES" : timed ? "TIME" : "POINTS", ink);
            DrawText2D(ModScoreColumn2, y, Align.Center, 0, freezeTag ? "FROZEN" : deaths ? "DEATHS" : "KILLS", ink);''')

freeze_rules = r'''using System;
using System.Runtime.CompilerServices;
using MphRead.Entities;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.Mods.FreezeTag;

public enum FreezeTagPhase : byte { Countdown, Active, Overtime, Intermission, Finished }

public static class FreezeTagRules
{
    public const int RulesetVersion = 1;
    public const int RoundWinLimit = 5;
    public const int DefaultRoundSeconds = 180;
    public const int CountdownTicks = 3 * 60;
    public const int IntermissionTicks = 5 * 60;
    public const int ThawTicks = 210;
    public const float ThawRadius = 2.75f;
    public const int LatePowerupSeconds = 30;
    public static bool Active(GameMode mode) => mode == GameMode.FreezeTag;
}

public sealed class FreezeTagState
{
    private readonly int[] _thaw = new int[PlayerEntity.SlotCapacity];
    private readonly int[,] _assist = new int[PlayerEntity.SlotCapacity, PlayerEntity.SlotCapacity];
    private bool _initialized;
    public FreezeTagPhase Phase { get; private set; } = FreezeTagPhase.Countdown;
    public int Round { get; private set; } = 1;
    public int RedWins { get; private set; }
    public int BlueWins { get; private set; }
    public int PhaseTicks { get; private set; } = FreezeTagRules.CountdownTicks;
    public int ElapsedTicks { get; private set; }
    public bool LatePowerupActive { get; private set; }
    public int ThawProgress(int slot) => (uint)slot < _thaw.Length ? _thaw[slot] : 0;

    private void Ensure(Scene scene)
    {
        if (_initialized) return;
        _initialized = true;
        foreach (var p in scene.GetPlayerEntities()) if (p.ModIsInPlay) p.ModFreezeTagRoundReset(false);
        Announce(scene, "FREEZE TAG - FREEZE THE ENTIRE ENEMY TEAM");
    }

    public void Tick(Scene scene)
    {
        Ensure(scene);
        if (Phase == FreezeTagPhase.Finished) return;
        if (Phase == FreezeTagPhase.Countdown)
        {
            LockPlayers(scene);
            if (--PhaseTicks <= 0) { Phase = FreezeTagPhase.Active; PhaseTicks = 0; Announce(scene, $"ROUND {Round}"); }
            return;
        }
        if (Phase == FreezeTagPhase.Intermission)
        {
            LockPlayers(scene);
            if (--PhaseTicks <= 0) BeginNextRound(scene);
            return;
        }
        ElapsedTicks++;
        HoldFrozen(scene);
        ProcessThaws(scene);
        if (EvaluateRound(scene)) return;
        int left = Math.Max(0, FreezeTagRules.DefaultRoundSeconds * 60 - ElapsedTicks);
        if (!LatePowerupActive && left <= FreezeTagRules.LatePowerupSeconds * 60)
        {
            LatePowerupActive = true;
            foreach (var p in scene.GetPlayerEntities()) if (p.ModIsInPlay && !p.ModFreezeTagFrozen) p.ModFreezeTagLatePower();
            Announce(scene, "LATE ROUND: DOUBLE DAMAGE ACTIVE");
        }
        if (left == 0) ResolveTimeout(scene);
    }

    private static void LockPlayers(Scene scene)
    {
        foreach (var p in scene.GetPlayerEntities()) if (p.ModIsInPlay) { p.Controls.ClearAll(); p.Speed = Vector3.Zero; }
    }
    private static void HoldFrozen(Scene scene)
    {
        foreach (var p in scene.GetPlayerEntities()) if (p.ModFreezeTagFrozen) p.ModFreezeTagHold();
    }
    private void ProcessThaws(Scene scene)
    {
        foreach (var target in scene.GetPlayerEntities())
        {
            if (!target.ModIsInPlay || !target.ModFreezeTagFrozen || target.SlotIndex < 0) continue;
            int rescuers=0;
            foreach (var ally in scene.GetPlayerEntities())
            {
                if (ally == target || !ally.ModIsInPlay || ally.ModFreezeTagFrozen || ally.Health <= 0 || ally.TeamIndex != target.TeamIndex || ally.SlotIndex < 0) continue;
                if ((ally.Position-target.Position).LengthSquared > FreezeTagRules.ThawRadius*FreezeTagRules.ThawRadius) continue;
                rescuers++; _assist[target.SlotIndex, ally.SlotIndex]++;
            }
            if (rescuers==0) continue;
            _thaw[target.SlotIndex]=Math.Min(FreezeTagRules.ThawTicks,_thaw[target.SlotIndex]+rescuers);
            if (_thaw[target.SlotIndex] >= FreezeTagRules.ThawTicks)
            {
                target.ModFreezeTagThaw(); _thaw[target.SlotIndex]=0;
                for(int i=0;i<PlayerEntity.SlotCapacity;i++) _assist[target.SlotIndex,i]=0;
            }
        }
    }
    private bool EvaluateRound(Scene scene)
    {
        Span<int> members=stackalloc int[2]; Span<int> frozen=stackalloc int[2];
        foreach(var p in scene.GetPlayerEntities())
        {
            if(!p.ModIsInPlay || p.TeamIndex<0 || p.TeamIndex>1) continue;
            members[p.TeamIndex]++; if(p.ModFreezeTagFrozen) frozen[p.TeamIndex]++;
        }
        for(int losing=0;losing<2;losing++) if(members[losing]>0 && frozen[losing]==members[losing]) { Award(scene,losing^1); return true; }
        return false;
    }
    private void ResolveTimeout(Scene scene)
    {
        Span<int> live=stackalloc int[2];
        foreach(var p in scene.GetPlayerEntities()) if(p.ModIsInPlay && p.TeamIndex>=0 && p.TeamIndex<2 && !p.ModFreezeTagFrozen) live[p.TeamIndex]++;
        if(live[0]==live[1]) { if(Phase!=FreezeTagPhase.Overtime){Phase=FreezeTagPhase.Overtime; Announce(scene,"OVERTIME");} return; }
        Award(scene,live[0]>live[1]?0:1);
    }
    private void Award(Scene scene,int winner)
    {
        if(winner==0) RedWins++; else BlueWins++;
        foreach(var p in scene.GetPlayerEntities()) if(p.ModIsInPlay && p.TeamIndex==winner) { scene.GameState.Points[p.SlotIndex]++; break; }
        scene.GameState.TeamPoints[0]=RedWins; scene.GameState.TeamPoints[1]=BlueWins;
        bool match=RedWins>=FreezeTagRules.RoundWinLimit || BlueWins>=FreezeTagRules.RoundWinLimit;
        Announce(scene,match?(winner==scene.Players.Main.TeamIndex?"MATCH WON":"MATCH LOST"):(winner==scene.Players.Main.TeamIndex?"ROUND WON":"ROUND LOST"));
        if(match){Phase=FreezeTagPhase.Finished; PhaseTicks=0; return;}
        Phase=FreezeTagPhase.Intermission; PhaseTicks=FreezeTagRules.IntermissionTicks;
    }
    private void BeginNextRound(Scene scene)
    {
        Round++; Phase=FreezeTagPhase.Countdown; PhaseTicks=FreezeTagRules.CountdownTicks; ElapsedTicks=0; LatePowerupActive=false;
        Array.Clear(_thaw); Array.Clear(_assist);
        foreach(var p in scene.GetPlayerEntities()) if(p.ModIsInPlay) p.ModFreezeTagRoundReset(true);
    }
    private static void Announce(Scene scene,string message)
    {
        if(!Mods.Headless.Active) scene.Players.Main.QueueHudMessage(128,70,2.5f,1,message);
    }
}

public static class FreezeTagWorld
{
    private static readonly ConditionalWeakTable<Scene,FreezeTagState> States=new();
    public static FreezeTagState Get(Scene scene)=>States.GetValue(scene,_=>new FreezeTagState());
}

public static class FreezeTagRuntime
{
    public static void Tick(Scene scene)
    {
        if(!FreezeTagRules.Active(scene.GameState.Mode) || scene.Services.IsReplica) return;
        if(NetSession.Active && !NetSession.IsAuthority && !NetSession.IsHost) return;
        FreezeTagWorld.Get(scene).Tick(scene);
    }
}
''';
write('src/MphRead/Mods/FreezeTag/FreezeTagRules.cs', freeze_rules)

player = r'''using System;
using MphRead.Formats;
using MphRead.Mods.FreezeTag;
using MphRead.Mods.Multiplayer;
using OpenTK.Mathematics;

namespace MphRead.Entities;

public partial class PlayerEntity
{
    private bool _freezeTagFrozen;
    internal bool ModFreezeTagFrozen => _freezeTagFrozen;

    internal void ModFreezeTagFreeze(PlayerEntity? attacker)
    {
        if(!FreezeTagRules.Active(_scene.GameState.Mode) || _freezeTagFrozen) return;
        _freezeTagFrozen=true; _health=1; _frozenTimer=UInt16.MaxValue; _frozenGfxTimer=UInt16.MaxValue; _drawIceLayer=true;
        Speed=Vector3.Zero; Controls.ClearAll(); EquipInfo.ChargeLevel=0;
        _scene.GameState.Deaths[SlotIndex]++;
        if(attacker!=null && attacker!=this && !TeamRules.AreAllies(attacker.TeamIndex,TeamIndex))
        {
            _scene.GameState.Kills[attacker.SlotIndex]++;
            if(attacker.IsMainPlayer) attacker.QueueHudMessage(128,70,2,1,"ENEMY FROZEN");
        }
        _soundSource.PlaySfx(SfxId.SHOTGUN_FREEZE);
        if(IsMainPlayer) QueueHudMessage(128,70,2,1,"FROZEN - STAY NEAR A TEAMMATE TO THAW");
    }
    internal void ModFreezeTagHold()
    {
        if(!_freezeTagFrozen) return;
        _health=Math.Max(1,_health); _frozenTimer=UInt16.MaxValue; _frozenGfxTimer=UInt16.MaxValue; _drawIceLayer=true;
        Controls.ClearAll();
    }
    internal void ModFreezeTagThaw()
    {
        if(!_freezeTagFrozen) return;
        _freezeTagFrozen=false; _frozenTimer=0; _frozenGfxTimer=0; _drawIceLayer=false; _health=Math.Max(1,_healthMax); _damageInvulnTimer=30;
        _soundSource.PlaySfx(SfxId.SHOTGUN_BREAK_FREEZE);
        if(IsMainPlayer) QueueHudMessage(128,70,2,1,"THAWED");
    }
    internal void ModFreezeTagHazardRelease()
    {
        if(!_freezeTagFrozen) return;
        _freezeTagFrozen=false; _frozenTimer=0; _frozenGfxTimer=0; _drawIceLayer=false;
        var point=GetRespawnPoint();
        if(point!=null) Spawn(point.Position,point.FacingVector,point.UpVector,point.NodeRef,respawn:true); else _health=Math.Max(1,_healthMax);
        _soundSource.PlaySfx(SfxId.SHOTGUN_BREAK_FREEZE);
    }
    internal void ModFreezeTagApplyKnockback(Vector3 direction)
    {
        if(!_freezeTagFrozen || !Single.IsFinite(direction.X) || !Single.IsFinite(direction.Y) || !Single.IsFinite(direction.Z)) return;
        if(direction.LengthSquared>.35f*.35f) direction=direction.Normalized()*.35f;
        Speed+=direction; if(Speed.LengthSquared>.7f*.7f) Speed=Speed.Normalized()*.7f;
    }
    internal void ModFreezeTagRoundReset(bool respawn)
    {
        _freezeTagFrozen=false; _frozenTimer=0; _frozenGfxTimer=0; _drawIceLayer=false; _doubleDmgTimer=0;
        if(respawn){var point=GetRespawnPoint(); if(point!=null){Spawn(point.Position,point.FacingVector,point.UpVector,point.NodeRef,respawn:true); return;}}
        _health=Math.Max(1,_healthMax);
    }
    internal void ModFreezeTagLatePower()
    {
        if(_freezeTagFrozen || Health<=0) return;
        _doubleDmgTimer=Math.Max(_doubleDmgTimer,(ushort)(FreezeTagRules.LatePowerupSeconds*60));
        if(IsMainPlayer) QueueHudMessage(128,92,2,1,"DOUBLE DAMAGE - LATE ROUND");
    }
}
''';
write('src/MphRead/Mods/FreezeTag/PlayerFreezeTag.cs', player)

write('FREEZE_TAG_TEST_BUILD.txt', '''Project Prime Freeze Tag playable test
Base: AntiNotAnti/Project-Prime ea017489bfc1299af1c85dbb3f1aa185d32a32f8
Mode: Freeze Tag (GameMode 27)
Core: lethal hit freezes; proximity thaw 3.5s; multiple rescuers accelerate; frozen-body knockback; hazards release; first-to-5 rounds; 30s late-round Double Damage; native team scoreboard labels; BattleTeams map layers.
This build is an actual ProjectPrime.exe test client, not the prior HTML simulator.
''')

# v1.1 launch-path repair: keep Freeze Tag recognized by every selector/label
# and give the native HUD a valid rules block instead of leaving _rulesInfo null.
edit('src/MphRead/Mods/Network/MatchTypeCatalog.cs',
'''            mode is GameMode.BattleTeams or GameMode.SurvivalTeams or GameMode.Capture
                or GameMode.BountyTeams or GameMode.NodesTeams or GameMode.DefenderTeams
                or GameMode.HardpointTeams or GameMode.KillConfirmedTeams;''',
'''            mode is GameMode.BattleTeams or GameMode.SurvivalTeams or GameMode.Capture or GameMode.FreezeTag
                or GameMode.BountyTeams or GameMode.NodesTeams or GameMode.DefenderTeams
                or GameMode.HardpointTeams or GameMode.KillConfirmedTeams;''')

edit('src/MphRead/Mods/Launcher/Portable/OfflineLaunch.cs',
'''                new("One in the Chamber", GameMode.OneInTheChamber),
                new("Kill Confirmed", GameMode.KillConfirmed),''',
'''                new("One in the Chamber", GameMode.OneInTheChamber),
                new("Freeze Tag", GameMode.FreezeTag),
                new("Kill Confirmed", GameMode.KillConfirmed),''')

edit('src/MphRead/Entities/Players/PlayerHud.cs',
'''            else if (mode == GameMode.Survival || mode == GameMode.SurvivalTeams || mode == GameMode.OneInTheChamber)
            {
                _rulesInfo = HudElements.RulesInfo[1];
            }''',
'''            else if (mode == GameMode.FreezeTag)
            {
                // Freeze Tag needs a real native rule-layout object before SetUpHud
                // formats the text below. Without this branch _rulesInfo remains null
                // and scene startup aborts back to the launcher.
                _rulesInfo = HudElements.RulesInfo[1];
            }
            else if (mode == GameMode.Survival || mode == GameMode.SurvivalTeams || mode == GameMode.OneInTheChamber)
            {
                _rulesInfo = HudElements.RulesInfo[1];
            }''')

edit('src/MphRead/Entities/Players/PlayerHud.cs',
'''                string line = mode == GameMode.GunGame ? i switch
                {''',
'''                string line = mode == GameMode.FreezeTag ? i switch
                {
                    0 => "FREEZE TAG",
                    1 => "Freeze every enemy player to win the round.",
                    2 => "Stay near frozen teammates to thaw them.",
                    _ => "First team to five rounds wins."
                } : mode == GameMode.GunGame ? i switch
                {''')

# Freeze Tag score rows use the existing native kill/death counters as
# freezes/times-frozen; team Points remain round wins.
edit('src/MphRead/Mods/Render/PlayerEntityTeamScoreboard.cs',
'''                    DrawText2D(ModScoreColumn1, y, Align.Center, 0,
                        TeamScoreValue(timed, _scene.GameState.TeamTime[team], _scene.GameState.TeamPoints[team]), visual.Color);
                    DrawText2D(ModScoreColumn2, y, Align.Center, 0,
                        (deaths ? _scene.GameState.TeamDeaths[team] : _scene.GameState.TeamKills[team]).ToString(), visual.Color);''',
'''                    DrawText2D(ModScoreColumn1, y, Align.Center, 0,
                        freezeTag ? _scene.GameState.TeamKills[team].ToString()
                            : TeamScoreValue(timed, _scene.GameState.TeamTime[team], _scene.GameState.TeamPoints[team]), visual.Color);
                    DrawText2D(ModScoreColumn2, y, Align.Center, 0,
                        (freezeTag ? _scene.GameState.TeamDeaths[team]
                            : deaths ? _scene.GameState.TeamDeaths[team] : _scene.GameState.TeamKills[team]).ToString(), visual.Color);''')
edit('src/MphRead/Mods/Render/PlayerEntityTeamScoreboard.cs',
'''                DrawText2D(ModScoreColumn1, y, Align.Center, 0,
                    TeamScoreValue(timed, _scene.GameState.Time[slot], _scene.GameState.Points[slot]), ink);
                DrawText2D(ModScoreColumn2, y, Align.Center, 0,
                    (deaths ? _scene.GameState.Deaths[slot] : _scene.GameState.Kills[slot]).ToString(), ink);''',
'''                DrawText2D(ModScoreColumn1, y, Align.Center, 0,
                    freezeTag ? _scene.GameState.Kills[slot].ToString()
                        : TeamScoreValue(timed, _scene.GameState.Time[slot], _scene.GameState.Points[slot]), ink);
                DrawText2D(ModScoreColumn2, y, Align.Center, 0,
                    (freezeTag ? _scene.GameState.Deaths[slot]
                        : deaths ? _scene.GameState.Deaths[slot] : _scene.GameState.Kills[slot]).ToString(), ink);''')

edit('src/MphRead/GameState.cs',
'''            if (Mode == GameMode.BattleTeams)
            {''',
'''            if (Mode == GameMode.BattleTeams || Mode == GameMode.FreezeTag)
            {''')

# Asset-free regression checks for the launch/catalog/session path.
edit('src/MphRead/Mods/Network/GameModeCheck.cs',
'''            var state = new SceneGameState(new ScenePlayerRegistry());
            foreach (GameMode previous in modes)''',
'''            var state = new SceneGameState(new ScenePlayerRegistry());
            MatchTypeDefinition freezeType = MatchTypeCatalog.GameTypes[MatchTypeCatalog.BaseModeIndex(GameMode.FreezeTag)];
            Check(freezeType.TeamOnly && freezeType.Resolve(MatchFormat.FreeForAll) == GameMode.FreezeTag
                && MatchTypeCatalog.NormalizeFormat(freezeType, MatchFormat.FreeForAll) == MatchFormat.Auto,
                "Freeze Tag is a team-only first-class game type");
            Check(MatchTypeCatalog.IsInternalTeamMode(GameMode.FreezeTag) && state.IsTeamMode(GameMode.FreezeTag),
                "Freeze Tag survives team-mode classification");
            state.ConfigureMatchMode(GameMode.FreezeTag, true);
            Check(state.Mode == GameMode.FreezeTag && state.Teams
                && state.PointGoal == Mods.FreezeTag.FreezeTagRules.RoundWinLimit
                && state.ModeState.Method == typeof(Mods.FreezeTag.FreezeTagRuntime).GetMethod(nameof(Mods.FreezeTag.FreezeTagRuntime.Tick)),
                "Freeze Tag selects its own runtime handler instead of Battle");
            var freezeSession = new SessionStatePacket { Match = new MatchDefinition
                { RoomKey = "MP1 SANCTORUS", Mode = GameMode.FreezeTag, Format = MatchFormat.Auto,
                    PointGoal = Mods.FreezeTag.FreezeTagRules.RoundWinLimit }, MaxPlayers = 8 };
            byte[] freezeBytes = new byte[SessionStatePacket.Size]; freezeSession.Write(freezeBytes);
            Check(SessionStatePacket.TryRead(freezeBytes, out var freezeRead)
                && freezeRead.Match.Mode == GameMode.FreezeTag,
                "Freeze Tag roundtrips through authoritative session state");
            foreach (GameMode previous in modes)''')

# Freeze Tag owns a per-round clock. Keep the engine's match clock disabled
# until five rounds are won, then set it to zero to enter Project Prime results.
c = (root/'src/MphRead/Mods/FreezeTag/FreezeTagRules.cs').read_text(encoding='utf-8')
c = c.replace('''        Ensure(scene);
        if (Phase == FreezeTagPhase.Finished) return;''',
'''        Ensure(scene);
        // The stock engine clock is a whole-match clock. Freeze Tag has a
        // separate clock per round, so a positive MatchTime would end the
        // entire match after the first round timer.
        if (Phase != FreezeTagPhase.Finished) scene.GameState.MatchTime = -1;
        if (Phase == FreezeTagPhase.Finished) return;''')
c = c.replace('''        if(match){Phase=FreezeTagPhase.Finished; PhaseTicks=0; return;}''',
'''        if(match){Phase=FreezeTagPhase.Finished; PhaseTicks=0; scene.GameState.MatchTime=0; return;}''')
(root/'src/MphRead/Mods/FreezeTag/FreezeTagRules.cs').write_text(c,encoding='utf-8')
print('patched FreezeTag round/match clock ownership')


# v1.2 lobby fix: a stock remote hosted server does not know GameMode 27 and
# silently falls back to Battle. Freeze Tag must use the matching server binary
# shipped beside this client until the public hosted fleet supports the mode.
write('src/MphRead/Mods/FreezeTag/FreezeTagLaunchPolicy.cs', r'''using System;

namespace MphRead.Mods.FreezeTag;

public static class FreezeTagLaunchPolicy
{
    public static bool RequiresMatchingLocalServer(GameMode mode) => mode == GameMode.FreezeTag;

    public static void Trace(string stage, GameMode mode)
        => Console.WriteLine($"[FREEZETAG TRACE] {stage} = {mode} ({(int)mode})");

    public static void RequirePreserved(string stage, GameMode expected, GameMode actual)
    {
        if (expected == GameMode.FreezeTag && actual != GameMode.FreezeTag)
            throw new InvalidOperationException(
                $"FREEZE TAG MODE LOSS DETECTED at {stage}: expected {expected} ({(int)expected}), got {actual} ({(int)actual)");
    }
}
''')

edit('src/MphRead/Mods/Launcher/Gui/CreateServerScreen.cs',
'''            GameMode mode = SelectedMode();
            var hunter = (Hunter)Enum.Parse(typeof(Hunter), _hunter.Value);''',
'''            GameMode mode = SelectedMode();
            Mods.FreezeTag.FreezeTagLaunchPolicy.Trace("CreateServer SelectedMode", mode);
            var hunter = (Hunter)Enum.Parse(typeof(Hunter), _hunter.Value);''')

edit('src/MphRead/Mods/Launcher/Gui/CreateServerScreen.cs',
'''            if (Dedicated)
            {
                await StartHere(name, player, hunter, maps, timeLimit, pointGoal);
                return;
            }
            await StartOnServer(name, player, hunter, mode, maps, timeLimit, pointGoal);''',
'''            // The public hosted fleet is still stock Project Prime and does
            // not know custom GameMode 27. Its HostPool therefore interprets
            // the unknown value as Battle. Never send Freeze Tag to that path:
            // the matching ProjectPrimeServer.exe is packaged beside this client.
            if (Mods.FreezeTag.FreezeTagLaunchPolicy.RequiresMatchingLocalServer(mode))
            {
                if (!LocalServer.Ready)
                {
                    Fail("Freeze Tag requires the matching ProjectPrimeServer.exe beside this client.");
                    return;
                }
                Mods.FreezeTag.FreezeTagLaunchPolicy.Trace("CreateServer LocalServer handoff", mode);
                await StartHere(name, player, hunter, maps, timeLimit, pointGoal);
                return;
            }
            if (Dedicated)
            {
                await StartHere(name, player, hunter, maps, timeLimit, pointGoal);
                return;
            }
            await StartOnServer(name, player, hunter, mode, maps, timeLimit, pointGoal);''')

edit('src/MphRead/Mods/Launcher/Gui/CreateServerScreen.cs',
'''        private async Task StartOnServer(string name, string player, Hunter hunter,
            GameMode mode, List<(string RoomKey, GameMode Mode)> maps,
            int timeLimit, ushort pointGoal)
        {
            if (_chosen == null)''',
'''        private async Task StartOnServer(string name, string player, Hunter hunter,
            GameMode mode, List<(string RoomKey, GameMode Mode)> maps,
            int timeLimit, ushort pointGoal)
        {
            if (Mods.FreezeTag.FreezeTagLaunchPolicy.RequiresMatchingLocalServer(mode))
            {
                throw new InvalidOperationException(
                    "FREEZE TAG MODE LOSS DETECTED: Freeze Tag cannot use a stock remote hosted server.");
            }
            if (_chosen == null)''')

edit('src/MphRead/Mods/Launcher/Gui/CreateServerScreen.cs',
'''            int port = await Task.Run(() => LocalServer.Start(name, maps,
                maxPlayers: PlayerEntity.SlotCapacity, timeLimit: timeLimit,''',
'''            if (maps.Count > 0) Mods.FreezeTag.FreezeTagLaunchPolicy.Trace("Local server requested mode", maps[0].Mode);
            int port = await Task.Run(() => LocalServer.Start(name, maps,
                maxPlayers: PlayerEntity.SlotCapacity, timeLimit: timeLimit,''')

edit('src/MphRead/Mods/Launcher/Gui/CreateServerScreen.cs',
'''            Finish(new LaunchPlan
            {
                Kind = LaunchKind.Online,''',
'''            if (maps.Count > 0)
            {
                GameMode serverMode = NetSession.ActiveMatchDefinition?.Mode ?? maps[0].Mode;
                Mods.FreezeTag.FreezeTagLaunchPolicy.Trace("Client lobby authoritative mode", serverMode);
                Mods.FreezeTag.FreezeTagLaunchPolicy.RequirePreserved(
                    "client lobby authoritative state", maps[0].Mode, serverMode);
            }
            Finish(new LaunchPlan
            {
                Kind = LaunchKind.Online,''',1)

# Remove silent unknown-mode-to-Battle behavior from this build's hosted child.
edit('src/MphRead/Mods/Network/HostedServerProcess.cs',
'''            GameMode mode = Enum.IsDefined(typeof(GameMode), request.Mode)
                ? (GameMode)request.Mode
                : GameMode.Battle;''',
'''            if (!Enum.IsDefined(typeof(GameMode), request.Mode))
            {
                reason = $"unsupported game mode id {request.Mode}; refusing instead of silently starting Battle";
                return null;
            }
            GameMode mode = (GameMode)request.Mode;
            if (mode == GameMode.FreezeTag)
                Mods.FreezeTag.FreezeTagLaunchPolicy.Trace("Hosted child request", mode);''')

edit('src/MphRead/Mods/Network/HostPool.cs',
'''            GameMode mode = Enum.IsDefined(typeof(GameMode), request.Mode)
                ? (GameMode)request.Mode
                : GameMode.Battle;''',
'''            if (!Enum.IsDefined(typeof(GameMode), request.Mode))
                return new HostReplyPacket { Reason = $"unsupported game mode id {request.Mode}" };
            GameMode mode = (GameMode)request.Mode;
            if (mode == GameMode.FreezeTag)
                Mods.FreezeTag.FreezeTagLaunchPolicy.Trace("HostPool request", mode);''')

# Trace the actual host request before it goes onto the wire.
edit('src/MphRead/Mods/Network/NetMaster.cs',
'''                var request = new HostRequestPacket
                {
                    Protocol = NetConfig.ProtocolVersion,''',
'''                var request = new HostRequestPacket
                {
                    Protocol = NetConfig.ProtocolVersion,''')
edit('src/MphRead/Mods/Network/NetMaster.cs',
'''                var datagram = new byte[1 + request.Length];
                datagram[0] = (byte)PacketType.HostRequest;''',
'''                if (mode == GameMode.FreezeTag)
                    Mods.FreezeTag.FreezeTagLaunchPolicy.Trace("HostRequest.Mode", (GameMode)request.Mode);
                var datagram = new byte[1 + request.Length];
                datagram[0] = (byte)PacketType.HostRequest;''')

# Dedicated/local server gets the exact mode from its rotation file.
edit('src/MphRead/Mods/Network/LocalServer.cs',
'''            LastError = null;
            HostedLibrary = null;''',
'''            LastError = null;
            HostedLibrary = null;
            if (rotation.Count > 0 && rotation[0].Mode == GameMode.FreezeTag)
                Mods.FreezeTag.FreezeTagLaunchPolicy.Trace("LocalServer.Start rotation", rotation[0].Mode);''')

# Dedicated end-to-end, asset-free launch pipeline check.
write('src/MphRead/Mods/FreezeTag/FreezeTagLaunchCheck.cs', r'''using System;
using MphRead.Mods.Launcher;
using MphRead.Mods.Network;

namespace MphRead.Mods.FreezeTag;

public static class FreezeTagLaunchCheck
{
    private static int _checks;
    private static void Check(bool ok, string message)
    {
        _checks++;
        if (!ok) throw new InvalidOperationException(message);
    }

    public static int Run()
    {
        try
        {
            _checks = 0;
            MatchTypeDefinition type =
                MatchTypeCatalog.GameTypes[MatchTypeCatalog.BaseModeIndex(GameMode.FreezeTag)];
            MatchFormat format = MatchTypeCatalog.NormalizeFormat(type, MatchFormat.FreeForAll);
            GameMode selected = type.Resolve(format);
            Console.WriteLine($"SELECTED: {selected} ({(int)selected})");
            Check(selected == GameMode.FreezeTag && format == MatchFormat.Auto,
                "Freeze Tag selection/format normalization changed the mode");
            Check(MatchTypeCatalog.BaseModeIndex(GameMode.FreezeTag) != 0
                && type.Label == "Freeze Tag", "Freeze Tag catalog lookup fell back to Battle");

            var settings = new MenuSettings { FriendlyFire = "off", AffinityWeapons = "off",
                Fiesta = "off", InstaGib = "off", LowTier = "off", NoImperialist = "off",
                BalancedMode = "off", ShadowFreeze = "off", SpawnProtection = "off",
                AutoReset = "off" };
            LaunchPlan plan = OfflineLaunch.Create(settings, "MP1 SANCTORUS",
                GameMode.FreezeTag, Hunter.Samus, 0, 1, 1);
            Check(plan.Mode == GameMode.FreezeTag && plan.MatchRules.Mode == GameMode.FreezeTag,
                "Offline launch plan changed Freeze Tag");

            var request = new HostRequestPacket
            {
                Protocol = NetConfig.ProtocolVersion, MaxPlayers = 8,
                Mode = (byte)GameMode.FreezeTag, TimeLimit = 180, PointGoal = 5,
                RoomKey = "MP1 SANCTORUS", ServerName = "Freeze Tag check",
                Policy = ServerSessionPolicy.Lobby, Format = MatchFormat.Auto,
                MapIdentity = NetworkMapIdentity.ForRoom("MP1 SANCTORUS"),
                Rotation = new[] { HostRotationEntry.ForRoom("MP1 SANCTORUS", GameMode.FreezeTag) },
                HostNonce = 1, HostCookie = 1
            };
            byte[] hostWire = new byte[request.Length];
            request.Write(hostWire);
            HostRequestPacket hostRead = HostRequestPacket.Read(hostWire);
            Console.WriteLine($"HOST REQUEST: {(GameMode)hostRead.Mode} ({hostRead.Mode})");
            Check(hostRead.Mode == (byte)GameMode.FreezeTag
                && hostRead.Rotation is { Count: 1 }
                && hostRead.Rotation[0].Mode == GameMode.FreezeTag,
                "Host request/rotation changed Freeze Tag");

            var match = new MatchDefinition { RoomKey = "MP1 SANCTORUS",
                Mode = GameMode.FreezeTag, Format = MatchFormat.Auto,
                PointGoal = 5, TimeLimitSeconds = 180 };
            Check(LobbyRules.ValidateDefinition(match, out string why) == LobbyResultCode.Ok,
                "Freeze Tag lobby definition rejected: " + why);
            var session = new SessionStatePacket { Match = match, MaxPlayers = 8,
                Policy = ServerSessionPolicy.Lobby, Phase = SessionPhase.Lobby };
            byte[] wire = new byte[SessionStatePacket.Size]; session.Write(wire);
            Check(SessionStatePacket.TryRead(wire, out var read)
                && read.Match.Mode == GameMode.FreezeTag, "Session packet changed Freeze Tag");
            Console.WriteLine($"SERVER LOBBY: {read.Match.Mode} ({(int)read.Match.Mode})");
            Console.WriteLine($"CLIENT LOBBY: {read.Match.Mode} ({(int)read.Match.Mode})");

            var state = new SceneGameState(new ScenePlayerRegistry());
            state.ConfigureMatchMode(read.Match.Mode, applyDefaults:true);
            Console.WriteLine($"START MATCH: {state.Mode} ({(int)state.Mode})");
            Console.WriteLine($"SCENE MODE: {state.Mode} ({(int)state.Mode})");
            Check(state.Mode == GameMode.FreezeTag && state.Teams
                && state.ModeState.Method == typeof(FreezeTagRuntime).GetMethod(nameof(FreezeTagRuntime.Tick)),
                "Scene did not retain Freeze Tag runtime");

            Check(FreezeTagLaunchPolicy.RequiresMatchingLocalServer(GameMode.FreezeTag)
                && !FreezeTagLaunchPolicy.RequiresMatchingLocalServer(GameMode.Battle),
                "Freeze Tag matching-server policy is wrong");

            Console.WriteLine($"[freezetaglaunchcheck] PASS {_checks} checks");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("[freezetaglaunchcheck] FAIL " + ex);
            return 1;
        }
    }
}
''')

edit('src/MphRead/Mods/ModEntry.cs',
'''            if (HasFlag(args, "gamemodecheck"))
            {
                Environment.ExitCode = Network.GameModeCheck.Run();
                return true;
            }''',
'''            if (HasFlag(args, "gamemodecheck"))
            {
                Environment.ExitCode = Network.GameModeCheck.Run();
                return true;
            }
            if (HasFlag(args, "freezetaglaunchcheck"))
            {
                Environment.ExitCode = FreezeTag.FreezeTagLaunchCheck.Run();
                return true;
            }''')

# Update build marker.
edit('FREEZE_TAG_TEST_BUILD.txt',
'''This build is an actual ProjectPrime.exe test client, not the prior HTML simulator.''',
'''Lobby fix v1.2: Freeze Tag uses the matching packaged ProjectPrimeServer.exe and refuses stock remote hosted fallback.
This build is an actual ProjectPrime.exe test client, not the prior HTML simulator.''')

print('Freeze Tag patch complete')
