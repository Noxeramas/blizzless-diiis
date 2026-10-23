using System;
using System.Buffers.Binary;
using System.Data.Common;
using System.Linq;
using NHibernate;
using NHibernate.Engine;
using NHibernate.SqlTypes;
using NHibernate.UserTypes;

namespace DiIiS_NA.Core.Storage
{
	/// <summary>
	/// Stores a ushort[] as little-endian bytes in a bytea column. Without it NHibernate serializes
	/// arrays with BinaryFormatter, which .NET 8 disables and later versions remove.
	/// </summary>
	public class UShortArrayUserType : IUserType
	{
		public object NullSafeGet(DbDataReader rs, string[] names, ISessionImplementor session, object owner)
		{
			if (NHibernateUtil.Binary.NullSafeGet(rs, names[0], session) is not byte[] bytes)
				return null;

			var values = new ushort[bytes.Length / sizeof(ushort)];
			for (int i = 0; i < values.Length; i++)
				values[i] = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(i * sizeof(ushort)));
			return values;
		}

		public void NullSafeSet(DbCommand cmd, object value, int index, ISessionImplementor session)
		{
			byte[] bytes = null;
			if (value is ushort[] values)
			{
				bytes = new byte[values.Length * sizeof(ushort)];
				for (int i = 0; i < values.Length; i++)
					BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(i * sizeof(ushort)), values[i]);
			}
			NHibernateUtil.Binary.NullSafeSet(cmd, bytes, index, session);
		}

		public Type ReturnedType => typeof(ushort[]);

		public SqlType[] SqlTypes => new SqlType[] { new BinarySqlType() };

		public new bool Equals(object x, object y) =>
			ReferenceEquals(x, y) || (x is ushort[] lhs && y is ushort[] rhs && lhs.SequenceEqual(rhs));

		public int GetHashCode(object x) => x is ushort[] values ? values.Aggregate(17, (hash, v) => hash * 31 + v) : 0;

		// Callers change the array in place, so NHibernate needs real copies to detect changes.
		public object DeepCopy(object value) => (value as ushort[])?.Clone();

		public bool IsMutable => true;

		public object Replace(object original, object target, object owner) => DeepCopy(original);

		public object Assemble(object cached, object owner) => DeepCopy(cached);

		public object Disassemble(object value) => DeepCopy(value);
	}
}
