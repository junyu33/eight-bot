namespace Assets.Scripts
{
    public static class GameConfig
    {
        public static bool PlayerFirst = true;
        public static EightGameState CurrentState = EightGameState.CreateStart(true);

        public static void ResetState()
        {
            CurrentState = EightGameState.CreateStart(PlayerFirst);
        }
    }
}
