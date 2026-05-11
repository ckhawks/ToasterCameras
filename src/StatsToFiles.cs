using System;
using System.IO;
using System.Threading.Tasks;
using HarmonyLib;
using Unity.Netcode;

namespace ToasterCameras;

public static class StatsToFiles
{
    private static string logsDirectory;
    private static int playingTimeLeft = 0;

    [HarmonyPatch(typeof(GameManager), "OnGameStateChanged")]
    public static class GameManagerServerOnGameStateTick
    {
        [HarmonyPostfix]
        public static void Postfix(GameManager __instance, GameState oldGameState, GameState newGameState)
        {
            GameState gs = newGameState;

            string clock = $"{gs.Tick / 60:D2}:{gs.Tick % 60:D2}";
            _ = WriteToFileAsync("clock.txt", clock);
            if (oldGameState.Phase == GamePhase.Play) playingTimeLeft = oldGameState.Tick;
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
            }
            else if (gs.Period <= 3)
            {
                period_name = $"Period {gs.Period}";
            }
            else if (gs.Period >= 4)
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
                case GamePhase.PreGame:
                    phase = "Pre Game";
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
                case GamePhase.Intermission:
                    phase = "Period Over";
                    break;
                case GamePhase.GameOver:
                    phase = "Game Over";
                    break;
                case GamePhase.PostGame:
                    phase = "Post Game";
                    break;
                case GamePhase.Play:
                    phase = gs.Period <= 3 ? $"Period {gs.Period}" : $"Overtime {gs.Period - 3}";
                    break;
            }
            _ = WriteToFileAsync("phase_name.txt", phase);
        }
    }

    private static async Task WriteToFileAsync(string filename, string data)
    {
        string filePath = Path.Combine(logsDirectory, filename);

        try
        {
            using (StreamWriter writer = new StreamWriter(filePath, false))
            {
                await writer.WriteAsync($"{data}");
            }
        }
        catch (Exception e)
        {
            Plugin.LogError($"Error writing to file: {e}");
        }
    }

    public static void Setup()
    {
        string rootPath = Path.GetFullPath(".");
        logsDirectory = Path.Combine(rootPath, "textfiles");

        if (!Directory.Exists(logsDirectory))
        {
            try
            {
                Directory.CreateDirectory(logsDirectory);
            }
            catch (IOException ex)
            {
                UnityEngine.Debug.LogError($"Failed to create directory: {logsDirectory}. Error: {ex.Message}");
            }
            catch (System.Security.SecurityException ex)
            {
                UnityEngine.Debug.LogError($"Security exception creating directory: {logsDirectory}. Error: {ex.Message}");
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogError($"An unexpected error occurred creating directory: {logsDirectory}. Error: {ex.Message}");
            }
        }
    }

    // b312 replaced Server_GoalScoredRpc with Server_NotifyGoalScoredRpc, which now
    // uses NetworkObjectReferences instead of (bool, ulong) pairs and drops the
    // last-player / speed parameters.
    [HarmonyPatch(typeof(GameManager), nameof(GameManager.Server_NotifyGoalScoredRpc))]
    public static class GameManagerServerNotifyGoalScoredRpc
    {
        [HarmonyPostfix]
        public static void Postfix(
            GameManager __instance,
            PlayerTeam byTeam,
            NetworkObjectReference goalPlayerNetworkObjectReference,
            NetworkObjectReference assistPlayerNetworkObjectReference,
            NetworkObjectReference secondAssistPlayerNetworkObjectReference,
            NetworkObjectReference puckNetworkObjectReference)
        {
            Plugin.Log($"GameManagerServerNotifyGoalScoredRpc (Postfix) Goal scored!");

            Player goalPlayer = NetworkingUtils.GetPlayerFromNetworkObjectReference(goalPlayerNetworkObjectReference);
            Player assistPlayer = NetworkingUtils.GetPlayerFromNetworkObjectReference(assistPlayerNetworkObjectReference);
            Player secondAssistPlayer = NetworkingUtils.GetPlayerFromNetworkObjectReference(secondAssistPlayerNetworkObjectReference);

            if (goalPlayer != null)
            {
                _ = WriteToFileAsync("goal_scorer.txt", goalPlayer.Username.Value.ToString());
                Plugin.Log($" - Scored by   : {goalPlayer.Username.Value}");
            }
            else
            {
                _ = WriteToFileAsync("goal_scorer.txt", "");
            }

            if (assistPlayer != null)
            {
                _ = WriteToFileAsync("goal_assister.txt", assistPlayer.Username.Value.ToString());
                Plugin.Log($" - Assisted by : {assistPlayer.Username.Value}");
            }
            else
            {
                _ = WriteToFileAsync("goal_assister.txt", "");
            }

            if (secondAssistPlayer != null)
            {
                _ = WriteToFileAsync("goal_assister2.txt", secondAssistPlayer.Username.Value.ToString());
                Plugin.Log($" - Assisted by : {secondAssistPlayer.Username.Value}");
            }
            else
            {
                _ = WriteToFileAsync("goal_assister2.txt", "");
            }

            _ = WriteToFileAsync("goal_team.txt", byTeam == PlayerTeam.Red ? "Red" : "Blue");
        }
    }
}
