using System.Collections.Generic;
using RimWorld;
using Verse;
using System.Linq;
using Verse.AI;
using UnityEngine;
using System;
using RimWorld.Planet;

namespace SPM{

    [DefOf]
    public static class spmJobDefs{
        public static JobDef SPM_BroadcastPropaganda;
    }

    [DefOf]
    public static class spmHistoryEventDefs{
        public static HistoryEventDef SPM_BroadcastPropagandaHistoryEvent;
    }

    class CompProperties_BroadcastPropaganda : CompProperties {

        [MustTranslate]
        #pragma warning disable 0649 //Disable unused warning
        public string useLabel;

        public int useDuration = 2500;

        public int minSocialLevel = 5;

        public CompProperties_BroadcastPropaganda(){
            this.compClass = typeof(BroadcastPropaganda);
        }

    }

    class BroadcastPropaganda : ThingComp{

        int tickLastUsed;
        int ticksCooldown = 60000; //1 Rimworld day

        public BroadcastPropaganda(){
            tickLastUsed = -1;
        }

        public CompProperties_BroadcastPropaganda Props{
            get{
                return (CompProperties_BroadcastPropaganda) this.props;
            }
        }

        protected virtual string FloatMenuOptionLabel{
            get{
                return this.Props.useLabel;
            }
        }

        public void setTickLastUsed(int tick){
            tickLastUsed = tick;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref tickLastUsed, "SPM_BroadcastPropagandaThingComp_TickLastUsed", -1);
        }

        public override IEnumerable<FloatMenuOption> CompFloatMenuOptions(Pawn myPawn){

            List<Thing> radioTowers = this.parent.Map.listerThings.ThingsMatching(ThingRequest.ForDef(ThingDef.Named("RadioTowerBasic")));
            int countTowers = radioTowers.Count;
            int countHavePower = radioTowers.Select(t => t.TryGetComp<CompPowerTrader>()).Where(cpt => cpt != null && cpt.PowerOn == true).Count();

            //Check if we have radio tower
            if(countTowers <= 0){
                yield return new FloatMenuOption(this.FloatMenuOptionLabel + " (" + "SPM_MissingRadioTower".Translate() + ")", null, MenuOptionPriority.Default, null, null, 0f, null, null);
            }
            //Check if ANY radio tower is powered
            else if(countHavePower <= 0){
                yield return new FloatMenuOption(this.FloatMenuOptionLabel + " (" + "SPM_NoTowersPowered".Translate() + ")", null, MenuOptionPriority.Default, null, null, 0f, null, null);
            }
            //Check if on cooldown
            else if(tickLastUsed > 0 && tickLastUsed + ticksCooldown > Find.TickManager.TicksGame){
                yield return new FloatMenuOption(this.FloatMenuOptionLabel + " (" + "SPM_BroadcastOnCooldown".Translate().Formatted(GenDate.ToStringTicksToPeriod((tickLastUsed + ticksCooldown - Find.TickManager.TicksGame)).Named("COOLDOWNTIME")) + ")", null, MenuOptionPriority.Default, null, null, 0f, null, null);
            }
            //Skill is disabled. 
            else if(myPawn.skills.GetSkill(SkillDefOf.Social).TotallyDisabled){
                yield return new FloatMenuOption(this.FloatMenuOptionLabel + " (" + "Incapable".Translate() + ")", null, MenuOptionPriority.Default, null, null, 0f, null, null);
            }
            //Skill is below allowed level
            else if(myPawn.skills.GetSkill(SkillDefOf.Social).Level <= this.Props.minSocialLevel){
                yield return new FloatMenuOption(this.FloatMenuOptionLabel + " (" + "UnderRequiredSkill".Translate(this.Props.minSocialLevel) + ")", null, MenuOptionPriority.Default, null, null, 0f, null, null);
            }
            //Parent is reserved
            else if(!myPawn.CanReserve(this.parent, 1, -1, null, false)){
                Pawn reserver = myPawn.Map.reservationManager.FirstRespectedReserver(this.parent, myPawn);
                yield return new FloatMenuOption(this.FloatMenuOptionLabel + " (" + "ReservedBy".Translate(myPawn.LabelShort, reserver) + ")", null, MenuOptionPriority.Default, null, null, 0f, null, null);
            }
            //Pawn can't reach device
            else if (!myPawn.CanReach(this.parent, PathEndMode.Touch, Danger.Deadly, false, false, TraverseMode.ByPawn))
			{
				yield return new FloatMenuOption(this.FloatMenuOptionLabel + " (" + "NoPath".Translate() + ")", null, MenuOptionPriority.Default, null, null, 0f, null, null);
			}
            else{
                yield return new FloatMenuOption(this.FloatMenuOptionLabel, delegate(){
                    if(myPawn.CanReserveAndReach(this.parent, PathEndMode.Touch, Danger.Deadly, 1, -1, null, false)){
                        Job job = JobMaker.MakeJob(spmJobDefs.SPM_BroadcastPropaganda, this.parent);
                        myPawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                    }
                }, MenuOptionPriority.Default, null, null, 0f, null, null);
            }

            yield break;
        }
    }

    public class JobDriver_BroadcastPropaganda : JobDriver
    {

        private int baseWork = -1;
        private float workLeft = 0;

        private int jobMinSocial = 5;

        private BroadcastPropaganda sourceComp;

        public override void Notify_Starting()
		{
			base.Notify_Starting();
            sourceComp = job.GetTarget(TargetIndex.A).Thing.TryGetComp<BroadcastPropaganda>();
			baseWork = sourceComp.Props.useDuration;
            jobMinSocial = sourceComp.Props.minSocialLevel;
		}

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            Pawn pawn = this.pawn;
            LocalTargetInfo targetA = this.job.targetA;
            Job job = this.job;
            return pawn.Reserve(targetA, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            //Step 1: Go to Comms Console interaction area
            yield return Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.InteractionCell).FailOn(delegate(Toil to){
                Building_CommsConsole console = (Building_CommsConsole)to.actor.jobs.curJob.GetTarget(TargetIndex.A).Thing;
                return !console.CanUseCommsNow;
            });
            //Step 2: Do "work" for a predefined number of ticks
            Toil broadcast = new Toil();
            broadcast.initAction = delegate(){
                this.workLeft =  (float) this.baseWork;
            };
            broadcast.tickAction = delegate(){
                //Decrease work left by 1
                this.workLeft -= 1;

                if(broadcast.actor.skills != null){
                    broadcast.actor.skills.Learn(SkillDefOf.Social, 0.1f, false);
                }
                if(this.workLeft <= 0f){
                    this.DoEffect(broadcast.actor);
                    this.ReadyForNextToil();
                    return;
                }
            };
            broadcast.WithProgressBar(TargetIndex.A, () => 1f - this.workLeft / (float) this.baseWork, false, -0.5f);
            broadcast.defaultCompleteMode = ToilCompleteMode.Never;
            broadcast.activeSkill = () => SkillDefOf.Social;
            yield return broadcast;
            yield break;
        }

        protected void DoEffect(Pawn actor){
            //Set tickLastUsed for the ThingComp
            sourceComp.setTickLastUsed(Find.TickManager.TicksGame);

            //We calculate the event type % here because it is the same value range for good or bad
            int eventType = Rand.RangeInclusive(0, 100);
            if(isGoodEvent(actor)){
                if(Prefs.DevMode){
                    Log.Message("[SPM] Event is good event!");
                }
                //60% of values
                //Increase goodwill of random faction where Goodwill >=0
                if(eventType <= 59){
                    Faction target;
                    if(!TryFindRandomFaction(out target, f => f.GoodwillWith(Faction.OfPlayer) >= 0)){
                        Log.Message("SPM: Cannot increase goodwill with random faction as no faction matches requirements");
                        return;
                    }
                    Faction.OfPlayer.TryAffectGoodwillWith(target, calculateGoodwillAdjustment(actor), true, true, spmHistoryEventDefs.SPM_BroadcastPropagandaHistoryEvent, null);
                    if(Prefs.DevMode){
                        Log.Message("[SPM] Tried to increase goodwill with " + target.Name);
                    }
                }
                //40% of values
                //A random pawn joins
                else{
                    if(Prefs.DevMode){
                        Log.Message("[SPM] Triggering pawn spawn");
                    }
                    //Trigger pawn generation
                    GeneratePawn(actor.Map);
                }
            }
            else{
                if(Prefs.DevMode){
                    Log.Message("[SPM] Event is bad event!");
                }
                //10% chance to decrease a random goodwill >= 0
                if(eventType <=9){
                    Faction target;
                    if(!TryFindRandomFaction(out target, f => f.GoodwillWith(Faction.OfPlayer) >= 0)){
                        Log.Message("SPM: Cannot increase goodwill with random faction as no faction matches requirements");
                        return;
                    }
                    Faction.OfPlayer.TryAffectGoodwillWith(target, -calculateGoodwillAdjustment(actor), true, true, spmHistoryEventDefs.SPM_BroadcastPropagandaHistoryEvent, null);
                    if(Prefs.DevMode){
                        Log.Message("[SPM] Tried to decrease goodwill with " + target.Name);
                    }
                }
                //40% chance to decrease a random goodwill <0
                else if(eventType <= 39){
                    Faction target;
                    if(!TryFindRandomFaction(out target, f => f.GoodwillWith(Faction.OfPlayer) < 0)){
                        Log.Message("SPM: Cannot increase goodwill with random faction as no faction matches requirements");
                        return;
                    }                

                    Faction.OfPlayer.TryAffectGoodwillWith(target, -calculateGoodwillAdjustment(actor), true, true, spmHistoryEventDefs.SPM_BroadcastPropagandaHistoryEvent, null);
                    if(Prefs.DevMode){
                        Log.Message("[SPM] Tried to decrease goodwill with " + target.Name);
                    }
                }
                //50% chance to trigger a raid
                else{
                    int raidDelayTicks = generateRandomDayDelayTicks();
                    GenerateRaid(actor, raidDelayTicks);
                }
            }
        }

        protected int calculateGoodwillAdjustment(Pawn actor){
            int pawnSocial = actor.skills.GetSkill(SkillDefOf.Social).Level;
            return (int) Math.Floor(5*Mathf.Log(pawnSocial, 20));
        }

        protected bool TryFindRandomFaction(out Faction faction, Func<Faction, bool> factionGoodwillFunction){
            return (from f in Find.FactionManager.AllFactions 
            where !f.def.hidden && !f.defeated && !f.IsPlayer && !f.IsPlayerGoodwillMinimum() && factionGoodwillFunction(f) 
            select f).TryRandomElement(out faction);
        }

        protected int generateRandomDayDelayTicks(){
            //60000 ticks in rimworld day
            return 60000 * Rand.RangeInclusive(1, 3);
        }

        protected float generatePawnSocialScaledRaidPoints(Pawn actor, float threatPoints){
             int pawnSocial = actor.skills.GetSkill(SkillDefOf.Social).Level;
             return (1+Mathf.Log(pawnSocial/jobMinSocial, 2))*threatPoints;
        }

        protected bool isGoodEvent(Pawn actor){
            int pawnSocial = actor.skills.GetSkill(SkillDefOf.Social).Level;
            float goodChance = (0.2f + Mathf.Log(pawnSocial/jobMinSocial, 17)) * 100;
            return Rand.RangeInclusive(0, 100) <= (int) goodChance;
        }

        protected void GeneratePawn(Map map){

            IntVec3 spawnSpot;

            //Try to find a spawn spot for the pawn, otherwise don't bother generating it
            if(!this.TryFindSpawnSpot(map, out spawnSpot)){
                Log.Error("[SPM] Failed to find spawn point for new pawn!");
                return;
            }

            //Create joiner
            PawnGenerationRequest req = new PawnGenerationRequest(PawnKindDefOf.Villager, Faction.OfPlayer, PawnGenerationContext.NonPlayer, -1, true, forceAddFreeWarmLayerIfNeeded: true, developmentalStages: DevelopmentalStage.Adult);
            Pawn joiner = PawnGenerator.GeneratePawn(req);

            //Send letter
            Letter letter = LetterMaker.MakeLetter("SPM_PawnJoinsTitle".Translate().Formatted(joiner.Named("PAWN")), "SPM_PawnJoinsText".Translate().Formatted(joiner.Named("PAWN")), LetterDefOf.PositiveEvent, null, null);
            Find.LetterStack.ReceiveLetter(letter);

            //Spawn joiner on map
            GenSpawn.Spawn(joiner, spawnSpot, map);
        }

        //Generate a raid
        protected void GenerateRaid(Pawn actor, int raidDelayTicks){

            //Check that we have a pawn. 
            if(actor == null){
                Log.Error("[SPM] No pawn found. Cannot generate raid");
                return;
            }

            Map map = actor.Map;
            IntVec3 spawnSpot;
            int @randInt = Rand.Int;

            //Find a spawn spot and an enemy faction
            if(!this.TryFindSpawnSpot(map, out spawnSpot)){
                Log.Error("[SPM] Failed to find spawn point for raid!");
                return;
            }

            //Generate raid incident
            IncidentParms raidParms = StorytellerUtility.DefaultParmsNow(IncidentCategoryDefOf.ThreatSmall, actor.Map);
            raidParms.forced = true;
            raidParms.raidArrivalMode = PawnsArrivalModeDefOf.EdgeWalkIn;
            raidParms.spawnCenter = spawnSpot;
            raidParms.points = generatePawnSocialScaledRaidPoints(actor, raidParms.points);
            raidParms.pawnGroupMakerSeed = new int?(@randInt);
            raidParms.raidStrategy = RaidStrategyDefOf.ImmediateAttack;

            if(!PawnGroupMakerUtility.TryGetRandomFactionForCombatPawnGroup(raidParms.points, out raidParms.faction)){
                Log.Error("SPM: Failed to find enemy faction!");
                return;
            }

            QueuedIncident qi = new QueuedIncident(new FiringIncident(IncidentDefOf.RaidEnemy, null, raidParms), Find.TickManager.TicksGame + raidDelayTicks, 0);
            Find.Storyteller.incidentQueue.Add(qi);

            //Send letter
            Letter letter = LetterMaker.MakeLetter("SPM_PropagandaTriggeredRaidTitle".Translate().Formatted(raidParms.faction.Name.Named("FACTION")), "SPM_PropagandaTriggeredRaidText".Translate().Formatted(raidParms.faction.Name.Named("FACTION"), GenDate.ToStringTicksToDays(raidDelayTicks, "F0").Named("DAYS")), LetterDefOf.NegativeEvent, null, null);
            Find.LetterStack.ReceiveLetter(letter);
        }

        private bool TryFindSpawnSpot(Map map, out IntVec3 spawnSpot){
            return CellFinder.TryFindRandomEdgeCellWith((IntVec3 c) => map.reachability.CanReachColony(c), map, CellFinder.EdgeRoadChance_Neutral, out spawnSpot);
        }

    }
}