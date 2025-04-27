using System;
using System.IO;
using System.Threading.Tasks;
using BepInEx;
using HarmonyLib;
using Unity.Netcode;

namespace ToasterCameras;

public static class StatsToFiles
{
    private static string logsDirectory;
    private static int playingTimeLeft = 0;
    
    [HarmonyPatch(typeof(GameManager), nameof(GameManager.OnGameStateChanged))]
    public static class GameManagerServerOnGameStateTick
    {
        [HarmonyPostfix]
        public static void Postfix(GameManager __instance, global::GameState oldGameState, global::GameState newGameState)
        {
            GameState gs = newGameState;
            
            string clock = $"{gs.Time / 60:D2}:{gs.Time % 60:D2}";
            _ = WriteToFileAsync("clock.txt", clock);
            if (oldGameState.Phase == GamePhase.Playing) playingTimeLeft = oldGameState.Time;
            // TODO ^^ this is one second behind because it's using oldGameState
            string clockReal = $"{playingTimeLeft / 60:D2}:{playingTimeLeft % 60:D2}";
            string clockRealMinusOneSecond = $"{Math.Max(playingTimeLeft - 1, 0) / 60:D2}:{Math.Max(playingTimeLeft - 1, 0) % 60:D2}";
            _ = WriteToFileAsync("realclock.txt", clockReal);
            _ = WriteToFileAsync("realclock-minusonesecond.txt", clockRealMinusOneSecond);
            _ = WriteToFileAsync("scorered.txt", gs.RedScore.ToString());
            _ = WriteToFileAsync("scoreblue.txt", gs.BlueScore.ToString());
            _ = WriteToFileAsync("period_number.txt", gs.Period.ToString());

            // If warmup, "Warmup"
            string period_name = "";
            if (gs.Phase == GamePhase.Warmup)
            {
                period_name = "Warmup";
            } else if (gs.Period <= 3)
            {
                period_name = $"Period {gs.Period}";
            } else if (gs.Period >= 4)
            {
                period_name = $"Overtime {gs.Period - 3}";
            }
            _ = WriteToFileAsync("period_name.txt", period_name);

            string phase = "";
            switch (gs.Phase)
            {
                case GamePhase.Warmup: 
                    phase = "Warmup";
                    break;
                case GamePhase.None:
                    phase = "None";
                    break;
                case GamePhase.Replay:
                    phase = "Replay";
                    break;
                case GamePhase.BlueScore:
                    phase = "Blue Score"; 
                    break;
                case GamePhase.RedScore:
                    phase = "Red Score";
                    break;
                case GamePhase.FaceOff:
                    phase = "Face Off";
                    break;
                case GamePhase.PeriodOver:
                    phase = "Period Over";
                    break;
                case GamePhase.GameOver:
                    phase = "Game Over";
                    break;
                case GamePhase.Playing:
                    phase = gs.Period <= 3 ? $"Period {gs.Period}" : $"Overtime {gs.Period - 3}";
                    break;
                
            }
            _ = WriteToFileAsync("phase_name.txt", phase);
        }
    }
    
    // Accepts a filename (not full path) and the data to write
    private static async Task WriteToFileAsync(string filename, string data)
    {
        string filePath = Path.Combine(logsDirectory, filename);

        try
        {
            // Use StreamWriter to write to the file in overwrite mode
            using (StreamWriter writer = new StreamWriter(filePath, false)) // 'false' for overwrite mode
            {
                await writer.WriteAsync($"{data}");  //Use WriteAsync instead of WriteLineAsync
            }

            // Plugin.Log.LogInfo($"Successfully wrote to file: {filePath} (replaced)");
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"Error writing to file: {e}");
        }
    }

    public static void Setup()
    {
        logsDirectory = Path.Combine(Paths.GameRootPath, "textfiles");
        
        // Check if the directory exists
        if (!Directory.Exists(logsDirectory))
        {
            // Create the directory if it doesn't exist
            try
            {
                Directory.CreateDirectory(logsDirectory);
            }
            catch (IOException ex)
            {
                // Handle potential exceptions (e.g., permissions issues)
                // Log the error or take appropriate action
                UnityEngine.Debug.LogError($"Failed to create directory: {logsDirectory}. Error: {ex.Message}");
            }
            catch (System.Security.SecurityException ex)
            {
                // Handle potential permission issues
                UnityEngine.Debug.LogError($"Security exception creating directory: {logsDirectory}. Error: {ex.Message}");
            }
            catch (System.Exception ex)
            {
                // Catch-all exception handler
                UnityEngine.Debug.LogError($"An unexpected error occurred creating directory: {logsDirectory}. Error: {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(GameManager), nameof(GameManager.Server_GoalScoredRpc))]
    public static class GameManagerServerGoalScoredRpc
    {
        [HarmonyPostfix]
        public static void Postfix(
            GameManager __instance, 
            PlayerTeam team, bool hasLastPlayer, ulong lastPlayerClientId, bool hasGoalPlayer, ulong goalPlayerClientId, bool hasAssistPlayer, ulong assistPlayerClientId, bool hasSecondAssistPlayer, ulong secondAssistPlayerClientId, float speedAcrossLine, float highestSpeedSinceStick, RpcParams rpcParams = null)
        {
            Plugin.Log.LogInfo($"GameManagerServerGoalScoredRpc (Postfix) Goal scored!");
            PlayerManager playerManager = NetworkBehaviourSingleton<PlayerManager>.Instance;
    
            if (playerManager == null)
            {
                Plugin.Log.LogError($"GameManagerServerGoalScoredRpc Postfix playerManager is null");
                return;
            }
            
            // Write goal scorer
            if (hasGoalPlayer)
            {
                Player player = playerManager.GetPlayerByClientId(goalPlayerClientId);
                _ = WriteToFileAsync("goal_scorer.txt", player.Username.Value.ToString());
                Plugin.Log.LogInfo($" - Scored by   : {player.Username.Value.ToString()}");
                
            }
            else
            {
                _ = WriteToFileAsync("goal_scorer.txt", "");
            }
            
            // Write assister
            if (hasAssistPlayer)
            {
                Player player = playerManager.GetPlayerByClientId(assistPlayerClientId);
                _ = WriteToFileAsync("goal_assister.txt", player.Username.Value.ToString());
                Plugin.Log.LogInfo($" - Assisted by : {player.Username.Value.ToString()}");
            }
            else
            {
                _ = WriteToFileAsync("goal_assister.txt", "");
            }
            
            _ = WriteToFileAsync("goal_team.txt", team == PlayerTeam.Red ? "Red" : "Blue");
        }
    }

    // [HarmonyPatch(typeof(UIAnnouncement), nameof(UIAnnouncement.ShowBlueTeamScoreAnnouncement))]
    // public static class UIAnnouncementShowBlueTeamScoreAnnouncement
    // {
    //     [HarmonyPostfix]
    //     public static void Postfix(
    //         UIAnnouncement __instance,
    //         float time,
    //         Player goalPlayer,
    //         Player assistPlayer,
    //         Player secondAssistPlayer)
    //     {
    //         // Write goal scorer
    //         if (goalPlayer != null)
    //         {
    //             _ = WriteToFileAsync("goal_scorer.txt", goalPlayer.Username.Value.ToString());
    //         }
    //         else
    //         {
    //             _ = WriteToFileAsync("goal_scorer.txt", "");
    //         }
    //         
    //         // Write assister
    //         if (assistPlayer != null)
    //         {
    //             _ = WriteToFileAsync("goal_assister.txt", assistPlayer.Username.Value.ToString());
    //         }
    //         else
    //         {
    //             _ = WriteToFileAsync("goal_assister.txt", "");
    //         }
    //         
    //         _ = WriteToFileAsync("goal_team.txt", "Blue");
    //     }
    // }
    //
    // [HarmonyPatch(typeof(UIAnnouncement), nameof(UIAnnouncement.ShowRedTeamScoreAnnouncement))]
    // public static class UIAnnouncementShowRedTeamScoreAnnouncement
    // {
    //     [HarmonyPostfix]
    //     public static void Postfix(
    //         UIAnnouncement __instance,
    //         float time,
    //         Player goalPlayer,
    //         Player assistPlayer,
    //         Player secondAssistPlayer)
    //     {
    //         // Write goal scorer
    //         if (goalPlayer != null)
    //         {
    //             _ = WriteToFileAsync("goal_scorer.txt", goalPlayer.Username.Value.ToString());
    //         }
    //         else
    //         {
    //             _ = WriteToFileAsync("goal_scorer.txt", "");
    //         }
    //         
    //         // Write assister
    //         if (assistPlayer != null)
    //         {
    //             _ = WriteToFileAsync("goal_assister.txt", assistPlayer.Username.Value.ToString());
    //         }
    //         else
    //         {
    //             _ = WriteToFileAsync("goal_assister.txt", "");
    //         }
    //         
    //         _ = WriteToFileAsync("goal_team.txt", "Red");
    //     }
    // }

    // [Error  :Il2CppInterop] During invoking native->managed trampoline
    // Exception: System.NullReferenceException: Object reference not set to an instance of an object.
    // at ToasterCameras.StatsToFiles.GameManagerClientOnGoalScoredRpc.Postfix(GameManager __instance, PlayerTeam team, Boolean hasLastPlayer, UInt64 lastPlayerClientId, Boolean hasGoalPlayer, UInt64 goalPlayerClientId, Boolean hasAssistPlayer, UInt64 assistPlayerClientId, Boolean hasSecondAssistPlayer, UInt64 secondAssistPlayerClientId, Single speedAcrossLine, Single highestSpeedSinceStick, RpcParams rpcParams)
    // at DMD<GameManager::Server_GoalScoredRpc>(GameManager this, PlayerTeam team, Boolean hasLastPlayer, UInt64 lastPlayerClientId, Boolean hasGoalPlayer, UInt64 goalPlayerClientId, Boolean hasAssistPlayer, UInt64 assistPlayerClientId, Boolean hasSecondAssistPlayer, UInt64 secondAssistPlayerClientId, Single speedAcrossLine, Single highestSpeedSinceStick, RpcParams rpcParams)
    // at (il2cpp -> managed) Server_GoalScoredRpc(IntPtr , PlayerTeam , Byte , UInt64 , Byte , UInt64 , Byte , UInt64 , Byte , UInt64 , Single , Single , IntPtr , Il2CppMethodInfo* )
}