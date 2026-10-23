namespace DiIiS_NA.Core.Storage
{
	public sealed class Config : Core.Config.Config
	{
		public string Root { get { return this.GetString("Root", "DataBase"); } set { this.Set("Root", value); } }
		public string MPQRoot { get { return this.GetString("MPQRoot", "DataBase/MPQ"); } set { this.Set("MPQRoot", value); } }
		/// <summary>Diablo III folder to read game data from; empty finds the installed game, "mpq" uses the bundled archives.</summary>
		public string GameDataPath { get { return this.GetString("GameDataPath", ""); } set { this.Set("GameDataPath", value); } }
		public bool EnableTasks { get { return this.GetBoolean("EnableTasks", true); } set { this.Set("EnableTasks", value); } }
		public bool LazyLoading { get { return this.GetBoolean("LazyLoading", false); } set { this.Set("LazyLoading", value); } }

		private static readonly Config _instance = new Config();
		public static Config Instance { get { return _instance; } }
		private Config() : base("Storage") { }
	}
}
