using System.Collections.Generic;
using System.IO;
using CrystalMpq;
using Gibbed.IO;
using DiIiS_NA.GameServer.Core.Types.SNO;
using DiIiS_NA.Core.MPQ.FileFormats.Types;

namespace DiIiS_NA.Core.MPQ.FileFormats
{
    [FileFormat(SNOGroup.Encounter)]
    public class Encounter : FileFormat
    {
        public Header Header { get; private set; }
        public int SNOSpawn { get; private set; }
        public List<EncounterSpawnOptions> Spawnoptions = new List<EncounterSpawnOptions>();

        public Encounter(IAssetFile file)
        {
            var stream = file.Open();
            Header = new Header(stream);
            SNOSpawn = stream.ReadValueS32();
            stream.Position += (2 * 4);// pad 2 int
            Spawnoptions = stream.ReadSerializedData<EncounterSpawnOptions>();
            stream.Close();
        }
    }

    public class EncounterSpawnOptions : ISerializableData
    {
        public int SNOSpawn { get; set; }
        public int Probability { get; set; }
        public int I1 { get; set; }
        public int SNOCondition { get; set; }

        public void Read(Stream stream)
        {
            SNOSpawn = stream.ReadValueS32();
            Probability = stream.ReadValueS32();
            I1 = stream.ReadValueS32();
            SNOCondition = stream.ReadValueS32();
        }
    }
}
