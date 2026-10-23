 using DiIiS_NA.Core.Logging;
 using DiIiS_NA.GameServer.Core.Types.SNO;
 using System;

namespace DiIiS_NA.Core.MPQ
{
    public abstract class Asset
    {
        public SNOGroup Group { get; private set; }
        public Int32 SNOId { get; private set; }
        public string Name { get; private set; }
        public string FileName { get; protected set; }
        public Type Parser { get; set; }

        protected FileFormat _data = null;
        private bool _parseFailed;
        protected static readonly Logger Logger = LogManager.CreateLogger("A");

        public FileFormat Data
        {
            get
            {
                if (_data == null && !_parseFailed && SourceAvailable && Parser != null)
                {
                    try
                    {
                        RunParser();
                    }
                    catch (Exception e)
                    {
                        _parseFailed = true; // a bad asset fails the same way every time; don't re-parse it on each access
                        Logger.FatalException(e, "Bad MPQ detected, failed parsing asset: {0}", this.FileName);
                    }
                }
                return _data;
            }
        }

        protected abstract bool SourceAvailable { get; }


        public Asset(SNOGroup group, Int32 snoId, string name)
        {
            this.FileName = group + "\\" + name + FileExtensions.Extensions[(int)group];
            this.Group = group;
            this.SNOId = snoId;
            this.Name = name;
        }

        public abstract void RunParser();
    }
}
