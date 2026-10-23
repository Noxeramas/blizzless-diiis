using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DiIiS_NA.GameServer.MessageSystem.Message.Definitions.Waypoint
{
    [Message(Opcodes.LastWaypointLevelAreaDataMessage, Consumers.Player)]
    public class LastWaypointLevelAreaDataMessage : GameMessage
    {
        public int /* sno */ SNOLevelArea;
        public int PlayerIndex;

        public LastWaypointLevelAreaDataMessage()
            : base(Opcodes.LastWaypointLevelAreaDataMessage)
        { }

        public override void Parse(GameBitBuffer buffer)
        {
            SNOLevelArea = buffer.ReadInt(32);
            PlayerIndex = buffer.ReadInt(32);
        }

        public override void Encode(GameBitBuffer buffer)
        {
            buffer.WriteInt(32, SNOLevelArea);
            buffer.WriteInt(32, PlayerIndex);
        }

        public override void AsText(StringBuilder b, int pad)
        {
            b.Append(' ', pad);
            b.AppendLine("LastWaypointLevelAreaDataMessage:");
            b.Append(' ', pad++);
            b.AppendLine("{");
            b.Append(' ', pad); b.AppendLine("SNOLevelArea: 0x" + SNOLevelArea.ToString("X8") + " (" + SNOLevelArea + ")");
            b.Append(' ', pad); b.AppendLine("PlayerIndex: 0x" + PlayerIndex.ToString("X8") + " (" + PlayerIndex + ")");
            b.Append(' ', --pad);
            b.AppendLine("}");
        }
    }
}
