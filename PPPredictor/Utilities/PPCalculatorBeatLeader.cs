﻿using beatleaderapi;
using PPPredictor.Data;
using SongCore.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace PPPredictor.Utilities
{
    public class PPCalculatorBeatLeader : PPCalculator
    {
        private const string AccRating = "AccRating";
        private const string PassRating = "PassRating";
        private const string TechRating = "TechRating";
        private readonly HttpClient httpClient = new HttpClient();
        private readonly beatleaderapi.beatleaderapi beatLeaderClient;
        private readonly double ppCalcWeight = 42;
        static List<(double, double)> accPointList = new List<(double, double)> {
                (1.0, 7.424),
                (0.999, 6.241),
                (0.9975, 5.158),
                (0.995, 4.010),
                (0.9925, 3.241),
                (0.99, 2.700),
                (0.9875, 2.303),
                (0.985, 2.007),
                (0.9825, 1.786),
                (0.98, 1.618),
                (0.9775, 1.490),
                (0.975, 1.392),
                (0.9725, 1.315),
                (0.97, 1.256),
                (0.965, 1.167),
                (0.96, 1.101),
                (0.955, 1.047),
                (0.95, 1.000),
                (0.94, 0.919),
                (0.93, 0.847),
                (0.92, 0.786),
                (0.91, 0.734),
                (0.9, 0.692),
                (0.875, 0.606),
                (0.85, 0.537),
                (0.825, 0.480),
                (0.8, 0.429),
                (0.75, 0.345),
                (0.7, 0.286),
                (0.65, 0.246),
                (0.6, 0.217),
                (0.0, 0.000) };

        public PPCalculatorBeatLeader() : base() 
        {
            beatLeaderClient = new beatleaderapi.beatleaderapi("https://api.beatleader.xyz/", httpClient);
        }

        protected override async Task<PPPPlayer> GetPlayerInfo(long userId)
        {
            try
            {
                var playerInfo = beatLeaderClient.PlayerAsync(userId.ToString(), false);
                var beatLeaderPlayer = await playerInfo;
                return new PPPPlayer(beatLeaderPlayer);
            }
            catch (Exception ex)
            {
                Plugin.Log?.Error($"PPCalculatorBeatLeader GetPlayerInfo Error: {ex.Message}");
                return new PPPPlayer(true);
            }
        }

        protected override async Task<List<PPPPlayer>> GetPlayers(double fetchIndexPage)
        {
            try
            {
                List<PPPPlayer> lsPlayer = new List<PPPPlayer>();
                PlayerResponseWithStatsResponseWithMetadata scoreSaberPlayerCollection = await beatLeaderClient.PlayersAsync("pp", (int)fetchIndexPage, 50, null, "desc", null, null, null, null, null, null, null, null, null, null);
                foreach (var scoreSaberPlayer in scoreSaberPlayerCollection.Data)
                {
                    lsPlayer.Add(new PPPPlayer(scoreSaberPlayer));
                }
                return lsPlayer;
            }
            catch (Exception ex)
            {
                Plugin.Log?.Error($"PPCalculatorBeatLeader GetPlayers Error: {ex.Message}");
                return new List<PPPPlayer>();
            }
        }

        protected override async Task<PPPScoreCollection> GetRecentScores(string userId, int pageSize, int page)
        {
            try
            {
                ScoreResponseWithMyScoreResponseWithMetadata scoreSaberCollection = await beatLeaderClient.ScoresAsync(userId, "date", "desc", page, pageSize, null, null, null, null, null);
                return new PPPScoreCollection(scoreSaberCollection);
            }
            catch (Exception ex)
            {
                Plugin.Log?.Error($"PPCalculatorBeatLeader GetRecentScores Error: {ex.Message}");
                return new PPPScoreCollection();
            }
        }

        public override double CalculatePPatPercentage(PPPBeatMapInfo currentBeatMapInfo, double percentage, bool levelFailed, GameplayModifiers gameplayModifiers)
        {
            try
            {
                percentage /= 100.0;
                List<string> lsModifiers = ParseModifiers(gameplayModifiers);
                double multiplier = GenerateModifierMultiplier(lsModifiers, currentBeatMapInfo.ModifierValueId, levelFailed, currentBeatMapInfo.BaseStarRating.ModifiersRating != null);
                if (multiplier != 0) //NF
                {
                    double passRating = currentBeatMapInfo.BaseStarRating.PassRating;
                    double techRating = currentBeatMapInfo.BaseStarRating.TechRating;
                    double accRating = currentBeatMapInfo.BaseStarRating.AccRating;

                    if(currentBeatMapInfo.BaseStarRating.ModifiersRating != null)
                    {
                        foreach (string modifier in lsModifiers.Select(x => x.ToLower()))
                        {
                            if (currentBeatMapInfo.BaseStarRating.ModifiersRating.ContainsKey(modifier + AccRating))
                            {
                                accRating = currentBeatMapInfo.BaseStarRating.ModifiersRating[modifier + AccRating];
                                passRating = currentBeatMapInfo.BaseStarRating.ModifiersRating[modifier + PassRating];
                                techRating = currentBeatMapInfo.BaseStarRating.ModifiersRating[modifier + TechRating];

                                break;
                            }
                        }
                    }

                    var (passPP, accPP, techPP) = CalculatePP(percentage, accRating * multiplier, passRating * multiplier, techRating * multiplier);
                    var rawPP = Inflate(passPP + accPP + techPP);
                    return rawPP;
                }
                return 0;
            }
            catch (Exception ex)
            {
                Plugin.Log?.Error($"PPCalculatorBeatLeader CalculatePPatPercentage Error: {ex.Message}");
                return -1;
            }
        }

        private (double, double, double) CalculatePP(double accuracy, double accRating, double passRating, double techRating)
        {
            double passPP = 15.2f * Math.Exp(Math.Pow(passRating, 1 / 2.62f)) - 30f;
            if (double.IsInfinity(passPP) || double.IsNaN(passPP) || double.IsNegativeInfinity(passPP) || passPP < 0)
            {
                passPP = 0;
            }
            double accPP = AccCurve(accuracy) * accRating * 34f;
            double techPP = Math.Exp(1.9f * accuracy) * techRating;

            return (passPP, accPP, techPP);
        }

        private double AccCurve(double acc)
        {
            int i = 0;
            for (; i < accPointList.Count; i++)
            {
                if (accPointList[i].Item1 <= acc)
                {
                    break;
                }
            }

            if (i == 0)
            {
                i = 1;
            }

            double middle_dis = (acc - accPointList[i - 1].Item1) / (accPointList[i].Item1 - accPointList[i - 1].Item1);
            return (accPointList[i - 1].Item2 + middle_dis * (accPointList[i].Item2 - accPointList[i - 1].Item2));
        }

        private double Inflate(double pp)
        {
            return (650f * Math.Pow(pp, 1.3f)) / Math.Pow(650f, 1.3f);
        }

        public override async Task<PPPBeatMapInfo> GetBeatMapInfoAsync(LevelSelectionNavigationController lvlSelectionNavigationCtrl, IDifficultyBeatmap beatmap)
        {
            try
            {
                if (lvlSelectionNavigationCtrl.selectedBeatmapLevel is CustomBeatmapLevel selectedCustomBeatmapLevel)
                {
                    string songHash = Hashing.GetCustomLevelHash(selectedCustomBeatmapLevel);
                    string searchString = CreateSeachString(songHash, "SOLO" + beatmap.parentDifficultyBeatmapSet.beatmapCharacteristic.serializedName, beatmap.difficultyRank);
                    ShortScore cachedInfo = _leaderboardInfo.LsLeaderboardScores?.FirstOrDefault(x => x.Searchstring == searchString);
                    bool refetchInfo = cachedInfo != null && cachedInfo.FetchTime < DateTime.Now.AddDays(-7);
                    if (cachedInfo == null || refetchInfo)
                    {
                        if (refetchInfo) _leaderboardInfo.LsLeaderboardScores?.Remove(cachedInfo);
                        Song song = await beatLeaderClient.Hash2Async(songHash);
                        if (song != null)
                        {
                            DifficultyDescription diff = song.Difficulties.FirstOrDefault(x => x.Value == beatmap.difficultyRank);
                            if (diff != null)
                            {
                                //Find or insert ModifierValueId
                                PPPModifierValues newModifierValues = new PPPModifierValues(diff.ModifierValues);
                                int modifierValueId = _leaderboardInfo.LsModifierValues.FindIndex(x => x.Equals(newModifierValues));
                                if(modifierValueId == -1)
                                {
                                    modifierValueId = _leaderboardInfo.LsModifierValues.Select(x => x.Id).DefaultIfEmpty(-1).Max() + 1;
                                    _leaderboardInfo.LsModifierValues.Add(new PPPModifierValues(modifierValueId, diff.ModifierValues));
                                }
                                _leaderboardInfo.LsLeaderboardScores.Add(new ShortScore(searchString, new PPPStarRating(diff), DateTime.Now, modifierValueId));
                                if (diff.Stars.HasValue && (int)diff.Status == (int)BeatLeaderDifficultyStatus.ranked)
                                {
                                    return new PPPBeatMapInfo(new PPPStarRating(diff), modifierValueId);
                                }
                            }
                        }
                    }
                    else
                    {
                        return new PPPBeatMapInfo(cachedInfo.StarRating, cachedInfo.ModifierValuesId);
                    }
                }
                return new PPPBeatMapInfo();
            }
            catch (Exception ex)
            {
                Plugin.Log?.Error($"PPCalculatorBeatLeader GetStarsForBeatmapAsync Error: {ex.Message}");
                return new PPPBeatMapInfo(new PPPStarRating(-1), -1);
            }
        }

        private List<string> ParseModifiers(GameplayModifiers gameplayModifiers)
        {
            try
            {
                List<string> lsModifiers = new List<string>();
                if (gameplayModifiers.disappearingArrows) lsModifiers.Add("DA");
                if (gameplayModifiers.songSpeed == GameplayModifiers.SongSpeed.Faster) lsModifiers.Add("FS");
                if (gameplayModifiers.songSpeed == GameplayModifiers.SongSpeed.Slower) lsModifiers.Add("SS");
                if (gameplayModifiers.songSpeed == GameplayModifiers.SongSpeed.SuperFast) lsModifiers.Add("SF");
                if (gameplayModifiers.ghostNotes) lsModifiers.Add("GN");
                if (gameplayModifiers.noArrows) lsModifiers.Add("NA");
                if (gameplayModifiers.noBombs) lsModifiers.Add("NB");
                if (gameplayModifiers.noFailOn0Energy) lsModifiers.Add("NF");
                if (gameplayModifiers.enabledObstacleType == GameplayModifiers.EnabledObstacleType.NoObstacles) lsModifiers.Add("NO");
                if (gameplayModifiers.proMode) lsModifiers.Add("PM");
                if (gameplayModifiers.smallCubes) lsModifiers.Add("SC");
                if (gameplayModifiers.instaFail) lsModifiers.Add("IF");
                //if (gameplayModifiers.FOURLIFES??) lsModifiers.Add("BE");
                if (gameplayModifiers.strictAngles) lsModifiers.Add("SA");
                if (gameplayModifiers.zenMode) lsModifiers.Add("ZM");
                return lsModifiers;
            }
            catch (Exception ex)
            {
                Plugin.Log?.Error($"PPCalculatorBeatLeader ParseModifiers Error: {ex.Message}");
                return new List<string>();
            }
        }

        private double GenerateModifierMultiplier(List<string> lsModifier, int modifierValueId, bool levelFailed, bool ignoreSpeedMultiplier)
        {
            try
            {
                double multiplier = 1;
                foreach (string modifier in lsModifier)
                {
                    if (!levelFailed && modifier == "NF") continue; //Ignore nofail until the map is failed in gameplay
                    if (ignoreSpeedMultiplier && (modifier == "SF" || modifier == "SS" || modifier == "FS")) continue; //Ignore speed multies and use the precomputed values from backend
                    multiplier += _leaderboardInfo.LsModifierValues[modifierValueId].DctModifierValues[modifier];
                }
                return multiplier;
            }
            catch (Exception ex)
            {
                Plugin.Log?.Error($"PPCalculatorBeatLeader GenerateModifierMultiplier Error: {ex.Message}");
                return -1;
            }
        }
    }
}
