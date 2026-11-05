using System;
using System.Collections.Generic;
using System.Linq;
using SandBox;
using SandBox.Missions.MissionLogics;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Source.Missions;
using TaleWorlds.MountAndBlade.Source.Missions.Handlers;

namespace AIChronicle
{
    /// <summary>只复用原版单挑视图，不带竞技场的强制击昏和整支部队胜负分发。</summary>
    internal sealed class DuelCampaignMissionComponent : CampaignMissionComponent
    {
        private readonly DuelMatch _match;
        public DuelCampaignMissionComponent(DuelMatch match) { _match = match; }
        public override void OnCreated() { if (_match.IsCurrent) base.OnCreated(); }
        public override void OnBehaviorInitialize() { if (_match.IsCurrent) base.OnBehaviorInitialize(); }
        public override void AfterStart() { if (_match.IsCurrent) base.AfterStart(); }
        public override void OnMissionTick(float dt) { if (_match.IsCurrent) base.OnMissionTick(dt); }
        protected override void OnEndMission() { if (_match.IsCurrent) base.OnEndMission(); }
        public override void OnMissionResultReady(MissionResult missionResult) { }
    }

    internal sealed class DuelMission : MissionLogic
    {
        private readonly DuelMatch _match;
        private readonly bool _arena;
        private Agent? _other;
        private Agent? _yours;
        private bool _ready;
        private bool _ending;
        private string? _failure;
        private float _resultWait;
        public DuelMission(DuelMatch match, bool arena) { _match = match; _arena = arena; }

        public static Mission Open(DuelMatch match)
        {
            var settlement = match.Session.Player.CurrentSettlement;
            var arena = settlement?.IsTown == true ? settlement.LocationComplex.GetLocationWithId("arena") : null;
            MissionInitializerRecord record;
            if (arena != null)
                record = SandBoxMissions.CreateSandBoxMissionInitializerRecord(arena.GetSceneName(settlement!.Town.GetWallLevel()), "", false, (DecalAtlasGroup)3);
            else
            {
                var patch = Campaign.Current.MapSceneWrapper.GetMapPatchAtPosition(MobileParty.MainParty.Position);
                var scene = Campaign.Current.Models.SceneModel.GetBattleSceneForMapPatch(patch, false);
                record = SandBoxMissions.CreateSandBoxMissionInitializerRecord(scene, "", false, (DecalAtlasGroup)2);
                record.SceneHasMapPatch = true;
                record.PatchCoordinates = patch.normalizedCoordinates;
                record.PatchEncounterDir = new Vec2(0, 1);
            }
            match.Scene = record.SceneName;
            UiTrace.Log("[Duel] " + match.Id + " opening scene=" + match.Scene + " arena=" + (arena != null)
                + " settlement=" + (settlement?.StringId ?? "-"));
            return MissionState.OpenNew("ArenaDuelMission", record, mission => CreateBehaviors(match, arena != null));
        }

        internal static MissionBehavior[] CreateBehaviors(DuelMatch match, bool arena)
        {
            var behaviors = new List<MissionBehavior>
            {
                new DuelMission(match, arena), new DuelCampaignMissionComponent(match), new MissionOptionsComponent(),
                new MissionFacialAnimationHandler(), new MissionAgentPanicHandler(), new AgentHumanAILogic(),
                new EquipmentControllerLeaveLogic()
            };
            // 原版该组件在 EarlyStart 无条件读取 Settlement.CurrentSettlement.Position。
            // 只有城镇竞技场需要其城镇道具/通道功能；野外双方由本任务直接生成。
            if (arena) behaviors.Add(new MissionAgentHandler());
            return behaviors.ToArray();
        }

        public override void EarlyStart()
            => UiTrace.Log("[Duel] " + _match.Id + " early-start arena=" + _arena);

        public override void AfterStart()
        {
            try
            {
                UiTrace.Log("[Duel] " + _match.Id + " after-start: preparing teams");
                if (!_match.IsCurrent) throw new OperationCanceledException("原战役已结束。");
                Mission.SetMissionMode(MissionMode.Battle, true);
                Mission.Teams.Add(BattleSideEnum.Attacker, _match.Session.Npc.MapFaction.Color, _match.Session.Npc.MapFaction.Color2,
                    _match.Session.Npc.Clan?.Banner, true);
                Mission.Teams.Add(BattleSideEnum.Defender, _match.Session.Player.MapFaction.Color, _match.Session.Player.MapFaction.Color2,
                    _match.Session.Player.Clan?.Banner, true);
                Mission.PlayerTeam = Mission.Teams.Defender;
                var positions = SpawnPositions();
                UiTrace.Log("[Duel] " + _match.Id + " spawn positions ready; spawning " + _match.Session.Player.StringId);
                _match.OtherOrigin = new DuelAgentOrigin(_match.Session.Player, MeetingSide.Other, _match.Session.Epoch);
                _match.YourOrigin = new DuelAgentOrigin(_match.Session.Npc, MeetingSide.Self, _match.Session.Epoch);
                _other = Spawn(_match.Session.Player, _match.OtherEquipment, _match.OtherOrigin, Mission.PlayerTeam, positions.Item1, positions.Item2);
                UiTrace.Log("[Duel] " + _match.Id + " spawning " + _match.Session.Npc.StringId);
                _yours = Spawn(_match.Session.Npc, _match.YourEquipment, _match.YourOrigin, Mission.PlayerEnemyTeam, positions.Item2, positions.Item1);
                _yours.SetIsAIPaused(true);
                DuelService.CombatReady(_match);
                _ready = true;
                _yours.SetIsAIPaused(false);
                _yours.SetWatchState(Agent.WatchState.Alarmed);
                UiTrace.Log("[Duel] " + _match.Id + " combat started; escrow held=" + _match.Escrow.Held);
                InformationManager.DisplayMessage(new InformationMessage("[决斗] 约定已锁定，开始交战。伤亡遵循当前战役设置。", Colors.Yellow));
            }
            catch (Exception ex) { _failure = ex.Message; _match.FailureReason = ex.Message; UiTrace.Log("Duel mission start failed: " + ex); }
        }

        private Tuple<Vec3, Vec3> SpawnPositions()
        {
            var candidates = new List<Vec3>();
            if (_arena)
            {
                foreach (var tag in new[] { "sp_arena", "sp_arena_respawn" })
                    candidates.AddRange(Mission.Scene.FindEntitiesWithTag(tag).Select(e => e.GetGlobalFrame().origin));
            }
            Mission.Scene.GetBoundingBox(out var min, out var max);
            var center = (min + max) * 0.5f;
            if (candidates.Count < 2)
            {
                var count = Mission.Scene.GetNavMeshFaceCount();
                for (var i = 0; i < count; i += Math.Max(1, count / 256))
                {
                    var position = Vec3.Zero;
                    Mission.Scene.GetNavMeshCenterPosition(i, ref position);
                    candidates.Add(position);
                }
            }
            if (candidates.Count == 0) throw new InvalidOperationException("场地没有可用的决斗位置。");
            var first = candidates.OrderBy(p => (p - center).LengthSquared).First();
            var second = candidates.Where(p => (p - first).LengthSquared >= 144 && (p - first).LengthSquared <= 1600)
                .OrderBy(p => Math.Abs((p - first).LengthSquared - 400)).ToList();
            var next = second.Count > 0 ? second[0] : Mission.GetRandomPositionAroundPoint(first, 12, 25);
            if ((next - first).LengthSquared < 64 || Mission.Scene.GetNavigationMeshForPosition(in next) == UIntPtr.Zero
                || Mission.Scene.GetNavigationMeshForPosition(in first) == UIntPtr.Zero)
                throw new InvalidOperationException("场地无法容纳两名决斗者。");
            first.z = Mission.Scene.GetGroundHeightAtPosition(first, (BodyFlags)0);
            next.z = Mission.Scene.GetGroundHeightAtPosition(next, (BodyFlags)0);
            return Tuple.Create(first, next);
        }

        private Agent Spawn(Hero hero, Equipment equipment, DuelAgentOrigin origin, Team team, Vec3 position, Vec3 opposite)
        {
            var direction = (opposite - position).AsVec2.Normalized();
            var build = new AgentBuildData(hero.CharacterObject).Team(team).Equipment(equipment).TroopOrigin(origin)
                .BodyProperties(hero.CharacterObject.GetBodyProperties(equipment, 0)).NoHorses(!_match.Mounted)
                .InitialPosition(in position).InitialDirection(in direction)
                .Controller(hero == _match.Session.Player ? AgentControllerType.Player : AgentControllerType.AI)
                .ClothingColor1(hero.MapFaction.Color).ClothingColor2(hero.MapFaction.Color2);
            var agent = Mission.SpawnAgent(build);
            agent.Health = Math.Min(hero.HitPoints, agent.HealthLimit);
            agent.FadeIn();
            return agent;
        }

        public override void OnAgentRemoved(Agent affectedAgent, Agent affectorAgent, AgentState state, KillingBlow blow)
        {
            if (!_ready || _ending || !_match.IsCurrent || (affectedAgent != _other && affectedAgent != _yours)) return;
            if (state != AgentState.Killed && state != AgentState.Unconscious && state != AgentState.Routed) return;
            var side = affectedAgent == _other ? MeetingSide.Other : MeetingSide.Self;
            var origin = side == MeetingSide.Other ? _match.OtherOrigin! : _match.YourOrigin!;
            if (state == AgentState.Killed) origin.SetKilled();
            else if (state == AgentState.Unconscious) origin.SetWounded();
            else origin.SetRouted(false);
            origin.OnAgentRemoved(affectedAgent.Health);
            _match.Resolution.ReportLoss(side, state == AgentState.Killed, state == AgentState.Routed);
        }

        public override void OnAgentHit(Agent affectedAgent, Agent affectorAgent, in MissionWeapon weapon, in Blow blow, in AttackCollisionData collision)
        {
            if (!_ready || _ending || !_match.IsCurrent || affectorAgent == null
                || (affectedAgent != _other && affectedAgent != _yours) || (affectorAgent != _other && affectorAgent != _yours)) return;
            affectorAgent.Origin.OnScoreHit(affectedAgent.Character, null, blow.InflictedDamage, affectedAgent.Health - blow.InflictedDamage < 1,
                false, weapon.CurrentUsageItem);
        }

        public override void OnMissionTick(float dt)
        {
            if (_ending) return;
            if (!_match.IsCurrent || _failure != null) { _ending = true; _match.Resolution.Seal(true); Mission.EndMission(); return; }
            if (!_ready || _match.Resolution.Observe() == DuelOutcome.Unresolved) return;
            _resultWait += dt;
            if (_resultWait < 0.2f) return;
            _match.Resolution.Seal();
            _ending = true;
            _yours?.SetIsAIPaused(true);
            Mission.EndMission();
        }

        public override InquiryData OnEndMissionRequest(out bool canPlayerLeave)
        {
            canPlayerLeave = true;
            if (!_ready || _ending || _match.Resolution.Sealed) return null!;
            if (_match.Resolution.Observe() != DuelOutcome.Unresolved) { canPlayerLeave = false; return null!; }
            return new InquiryData("认输并结束决斗", "离场按败处理，履行对方获胜后的约定；本场不发声望或影响力奖励。",
                true, true, "认输", "继续交战", () =>
                {
                    if (!_match.IsCurrent || _ending || _match.Resolution.Sealed) return;
                    InformationManager.HideInquiry();
                    _match.Resolution.ReportLoss(MeetingSide.Other, false, true); _match.Resolution.Seal();
                    _ending = true; Mission.EndMission();
                }, () => { });
        }

        public override void OnRetreatMission() => Forfeit();
        public override void OnSurrenderMission() => Forfeit();
        private void Forfeit()
        {
            if (!_ready || _ending || !_match.IsCurrent || _match.Resolution.Sealed) return;
            if (_match.Resolution.Observe() == DuelOutcome.Unresolved)
                _match.Resolution.ReportLoss(MeetingSide.Other, false, true);
            _match.Resolution.Seal();
            _ending = true;
        }

        protected override void OnEndMission()
        {
            _ending = true;
            if (_other?.IsActive() == true) _match.OtherOrigin?.OnAgentRemoved(_other.Health);
            if (_yours?.IsActive() == true) _match.YourOrigin?.OnAgentRemoved(_yours.Health);
            if (!_match.Resolution.Sealed) _match.Resolution.Seal(aborted: !_ready || _failure != null);
            DuelService.MissionEnded(_match);
        }
    }
}
