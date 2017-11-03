using HackstoneAnalyzer.PayloadFormat;
using PegasusUtil;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Hooks.PacketDumper
{
	class PegasusPacketDecoder
	{
		private Map<long, Type> _pegasusTypes;
		private Map<Type, uint> _pegasusHashes;

		public PegasusPacketDecoder()
		{
			Setup();
		}

		#region SETUP

		private void Setup()
		{
			_pegasusTypes = DiscoverWTCGPackets();
			_pegasusHashes = HashAllIProtoBuffClasses();
		}

		private Map<long, Type> DiscoverWTCGPackets()
		{
			var WTCGTypes = new Map<long, Type>();

			// Load assembly containing all related packets.
			// This assembly should be the CSharp-firstpass.
			Assembly container = typeof(Achieve).Assembly;
			IEnumerable<Type> allPacketIDs = container.GetTypes().Where(t => t.Name.Equals("PacketID"));
			foreach (Type packetIDType in allPacketIDs)
			{
				if (!packetIDType.IsEnum) continue;

				// Get the parent type, this holds the blueprint of the packet.
				Type parent = packetIDType.DeclaringType;
				// The underlying type of the enum is important because we want to convert it to
				// a long.
				Type enumUnderlyingType = Enum.GetUnderlyingType(packetIDType);
				// The enum property ID of packetIDType holds the identification value.
				string[] enumNames = Enum.GetNames(packetIDType);
				Array enumValues = Enum.GetValues(packetIDType);

				for (int i = 0; i < enumNames.Length; ++i)
				{
					string enumPropName = enumNames[i];
					if (enumPropName.Equals("ID"))
					{
						object propertyValue = enumValues.GetValue(i);
						long propLongValue = Convert.ToInt64(Convert.ChangeType(propertyValue, enumUnderlyingType));
						// Attach the parent type to the found identification number.
						WTCGTypes[propLongValue] = parent;
						// Goto next enum type.
						break;
					}
				}
			}

			return WTCGTypes;
		}

		// Hash all IProtoBuff classes and store them into the given container.
		// The hashes are used to identify packet payloads.
		private Map<Type, uint> HashAllIProtoBuffClasses()
		{
			var container = new Map<Type, uint>();

			foreach (Type protoClass in GetAllIProtoClasses())
			{
				uint hash = Util.GenerateHashFromName(protoClass.FullName);
				container[protoClass] = hash;
			}

			return container;
		}

		// Discover, dynamically, all classes that implement IProtoBuf.
		private IEnumerable<Type> GetAllIProtoClasses()
		{
			Type parentInterface = typeof(IProtoBuf); // This statement should load the Game DLL.
			Func<Type, bool> predicate = (Type t) => parentInterface.IsAssignableFrom(t) && t.IsClass;

			// The search must happen in all loaded assemblies
			return AppDomain.CurrentDomain.GetAssemblies()
				.SelectMany(a => a.GetTypes())
				.Where(predicate)
				.ToList();
		}

		#endregion

		public uint GetPegTypeHash(long packetType)
		{
			Type targetType;
			_pegasusTypes.TryGetValue(packetType, out targetType);
			if (targetType == null) return 0;

			uint targetHash = 0;
			_pegasusHashes.TryGetValue(targetType, out targetHash);
			return targetHash;
		}

		public bool CanDecodePacket(long packetType)
		{
			return _pegasusTypes.ContainsKey(packetType);
		}

		// Converts the body byte buffer into a deserialized object.
		// Returns null if the deserialization failed.
		public PegasusPacket DecodePacket(PegasusPacket packet)
		{
			Type targetType;
			_pegasusTypes.TryGetValue(packet.Type, out targetType);
			if (targetType == null)
			{
				return null;
			}

			// Setup decoding for the packet data
			Type decoder = typeof(ProtobufUtil);
			MethodInfo decoderMethod = decoder.GetMethod("ParseFromGeneric", BindingFlags.Public | BindingFlags.Static);
			// Specialize the method to fabricate the detected type.
			MethodInfo specializedDecoder = decoderMethod?.MakeGenericMethod(targetType);
			if (specializedDecoder == null)
			{
				return null;
			}

			var protoMessage = (IProtoBuf)specializedDecoder.Invoke(null, new object[] { packet.GetBody() });
			return new PegasusPacket(packet.Type, 0, protoMessage);
		}
	}
}
