namespace DiIiS_NA.Core.MPQ
{
	/// <summary>
	/// Which data-file layouts the parsers should expect. The bundled MPQ archives are 2.7.4; data read from the
	/// installed game uses the 2.8.0 layouts. 2.8.0 record sizes and offsets come from the 2.8.0.99920 client's
	/// own type descriptors (tools/protocol/dumptypes.py).
	/// </summary>
	public static class GameDataFormat
	{
		public static bool IsInstalledGame { get; internal set; }

		/// <summary>The 2.8.0 record size when reading the installed game's data, otherwise 0 (read as before).</summary>
		public static int RecordSize(int size280) => IsInstalledGame ? size280 : 0;
	}
}
