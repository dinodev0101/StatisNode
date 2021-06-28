﻿using System;
using System.Collections.Generic;
using System.Net;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;
using NLog;
using Stratis.Bitcoin.Features.Interop.Models;
using Stratis.Bitcoin.Utilities.JsonErrors;
using Stratis.Features.FederatedPeg.Conversion;
using Stratis.Features.FederatedPeg.Coordination;

namespace Stratis.Bitcoin.Features.Interop.Controllers
{
    [ApiVersion("1")]
    [Route("api/[controller]")]
    public sealed class InteropController : Controller
    {
        private readonly IConversionRequestRepository conversionRequestRepository;
        private readonly ICoordinationManager coordinationManager;
        private readonly ILogger logger;

        public InteropController(
            IConversionRequestRepository conversionRequestRepository,
            ICoordinationManager coordinationManager)
        {
            this.conversionRequestRepository = conversionRequestRepository;
            this.coordinationManager = coordinationManager;

            this.logger = LogManager.GetCurrentClassLogger();
        }

        [Route("status")]
        [HttpGet]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        [ProducesResponseType((int)HttpStatusCode.InternalServerError)]
        public IActionResult InteropStatus()
        {
            try
            {
                var response = new InteropStatusResponseModel();

                var mintRequests = new List<ConversionRequestModel>();

                foreach (ConversionRequest request in this.conversionRequestRepository.GetAllMint(false))
                {
                    mintRequests.Add(new ConversionRequestModel()
                    {
                        RequestId = request.RequestId,
                        RequestType = request.RequestType,
                        RequestStatus = request.RequestStatus,
                        BlockHeight = request.BlockHeight,
                        DestinationAddress = request.DestinationAddress,
                        Amount = request.Amount,
                        Processed = request.Processed,
                        Status = request.RequestStatus.ToString(),
                    });
                }

                response.MintRequests = mintRequests;

                var burnRequests = new List<ConversionRequestModel>();

                foreach (ConversionRequest request in this.conversionRequestRepository.GetAllBurn(false))
                {
                    burnRequests.Add(new ConversionRequestModel()
                    {
                        RequestId = request.RequestId,
                        RequestType = request.RequestType,
                        RequestStatus = request.RequestStatus,
                        BlockHeight = request.BlockHeight,
                        DestinationAddress = request.DestinationAddress,
                        Amount = request.Amount,
                        Processed = request.Processed,
                        Status = request.RequestStatus.ToString(),
                    });
                }

                response.BurnRequests = burnRequests;

                var receivedVotes = new Dictionary<string, List<string>>();

                foreach ((string requestId, HashSet<PubKey> pubKeys) in this.coordinationManager.GetStatus())
                {
                    var pubKeyList = new List<string>();

                    foreach (PubKey pubKey in pubKeys)
                    {
                        pubKeyList.Add(pubKey.ToHex());
                    }

                    receivedVotes.Add(requestId, pubKeyList);
                }

                response.ReceivedVotes = receivedVotes;

                return this.Json(response);
            }
            catch (Exception e)
            {
                this.logger.Error("Exception occurred: {0}", e.ToString());
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, e.Message, e.ToString());
            }
        }
    }
}
