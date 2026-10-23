using System.IO;

namespace DiIiS_NA.Core.MPQ
{
	/// <summary>
	/// A game data file the asset parsers read from, whether it comes from the installed game's CASC
	/// storage or from the bundled MPQ archives.
	/// </summary>
	public interface IAssetFile
	{
		string Name { get; }
		long Size { get; }
		Stream Open();
	}

	public sealed class MpqAssetFile : IAssetFile
	{
		private readonly CrystalMpq.MpqFile _file;

		public MpqAssetFile(CrystalMpq.MpqFile file) => _file = file;

		public string Name => _file.Name;
		public long Size => _file.Size;
		public Stream Open() => _file.Open();
	}
}
