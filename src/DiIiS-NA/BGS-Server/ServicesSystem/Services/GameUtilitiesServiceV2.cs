using DiIiS_NA.Core.Logging;
using Google.ProtocolBuffers;
using System;
using System.Collections.Generic;
using System.Linq;
using V1 = bgs.protocol;
using V2 = bgs.protocol.v2;
using V2Utilities = bgs.protocol.game_utilities.v2.client;

namespace DiIiS_NA.LoginServer.ServicesSystem.Services
{
    /// <summary>
    /// GameUtilities v2, which 2.8.0 clients use instead of v1's ProcessClientRequest. Each task is
    /// translated to the v1 request shape and handled by <see cref="GameUtilitiesService"/>.
    /// </summary>
    [Service(serviceID: 0x39, serviceName: "bnet.protocol.game_utilities.v2.client.GameUtilities")]
    public class GameUtilitiesServiceV2 : V2Utilities.GameUtilitiesService, IServerService
    {
        private static readonly Logger Logger = LogManager.CreateLogger();

        private const uint V1ServiceId = 0x38;

        public override void ProcessTask(IRpcController controller, V2Utilities.ProcessTaskRequest request,
            Action<V2Utilities.ProcessTaskResponse> done)
        {
            Logger.Debug("ProcessTask attributes: {0} | payload: {1}",
                Describe(request.AttributeList), Describe(request.PayloadList));

            var attributes = request.AttributeList.Concat(request.PayloadList).ToList();
            var id = attributes.FirstOrDefault(a => a.Name == "CustomMessageId");
            var message = attributes.FirstOrDefault(a => a.Name == "CustomMessage");
            if (id == null)
            {
                Logger.Warn("ProcessTask without a CustomMessageId; sending an empty result.");
                done(V2Utilities.ProcessTaskResponse.DefaultInstance);
                return;
            }

            long messageId = id.Value.HasUintValue ? (long)id.Value.UintValue : id.Value.IntValue;

            // The v1 handler reads the message id from attribute 1 and the message from attribute 2.
            var v1Request = V1.game_utilities.v1.ClientRequest.CreateBuilder()
                .AddAttribute(V1Attribute("CustomMessageDestination", V1.Variant.CreateBuilder().SetIntValue(0)))
                .AddAttribute(V1Attribute("CustomMessageId", V1.Variant.CreateBuilder().SetIntValue(messageId)))
                .AddAttribute(V1Attribute("CustomMessage",
                    V1.Variant.CreateBuilder().SetMessageValue(message?.Value.BlobValue ?? ByteString.Empty)))
                .Build();

            var v1Service = (GameUtilitiesService)Service.GetByID(V1ServiceId);
            v1Service.ProcessClientRequest(controller, v1Request, v1Response =>
            {
                var response = V2Utilities.ProcessTaskResponse.CreateBuilder();
                foreach (var attribute in v1Response.AttributeList)
                    response.AddResult(V2.Attribute.CreateBuilder().SetName(attribute.Name).SetValue(ToV2(attribute.Value)));

                var built = response.Build();
                Logger.Debug("ProcessTask({0}) result: {1}", messageId, Describe(built.ResultList));
                done(built);
            });
        }

        public override void GetAllValuesForAttribute(IRpcController controller,
            V2Utilities.GetAllValuesForAttributeRequest request, Action<V2Utilities.GetAllValuesForAttributeResponse> done)
        {
            Logger.Debug("GetAllValuesForAttribute({0}) is not implemented; sending no values.", request.AttributeKey);
            done(V2Utilities.GetAllValuesForAttributeResponse.DefaultInstance);
        }

        private static V1.Attribute.Builder V1Attribute(string name, V1.Variant.Builder value) =>
            V1.Attribute.CreateBuilder().SetName(name).SetValue(value);

        private static V2.Variant ToV2(V1.Variant value)
        {
            var variant = V2.Variant.CreateBuilder();
            if (value.HasMessageValue) variant.SetBlobValue(value.MessageValue);
            else if (value.HasBlobValue) variant.SetBlobValue(value.BlobValue);
            else if (value.HasIntValue) variant.SetIntValue(value.IntValue);
            else if (value.HasUintValue) variant.SetUintValue(value.UintValue);
            else if (value.HasStringValue) variant.SetStringValue(value.StringValue);
            else if (value.HasBoolValue) variant.SetBoolValue(value.BoolValue);
            else if (value.HasFloatValue) variant.SetFloatValue(value.FloatValue);
            return variant.Build();
        }

        private static string Describe(IEnumerable<V2.Attribute> attributes) =>
            string.Join(", ", attributes.Select(a => $"{a.Name}={Describe(a.Value)}"));

        private static string Describe(V2.Variant value) =>
            value.HasIntValue ? $"int {value.IntValue}" :
            value.HasUintValue ? $"uint {value.UintValue}" :
            value.HasStringValue ? $"string '{value.StringValue}'" :
            value.HasBlobValue ? $"blob ({value.BlobValue.Length} bytes)" :
            value.HasBoolValue ? $"bool {value.BoolValue}" :
            value.HasFloatValue ? $"float {value.FloatValue}" : "empty";
    }
}
