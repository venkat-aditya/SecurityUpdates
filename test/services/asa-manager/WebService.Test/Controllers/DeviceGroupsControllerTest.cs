// <copyright file="DeviceGroupsControllerTest.cs" company="3M">
// Copyright (c) 3M. All rights reserved.
// </copyright>

using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Mmm.Iot.AsaManager.Services;
using Mmm.Iot.AsaManager.Services.External.IotHubManager;
using Mmm.Iot.AsaManager.Services.Models;
using Mmm.Iot.AsaManager.WebService.Controllers;
using Mmm.Iot.Common.Services;
using Mmm.Iot.Common.Services.External.BlobStorage;
using Mmm.Iot.Common.Services.External.StorageAdapter;
using Mmm.Iot.Common.Services.Wrappers;
using Mmm.Iot.Common.TestHelpers;
using Moq;
using Xunit;

namespace Mmm.IoT.AsaManager.WebService.Test.Controllers
{
    public class DeviceGroupsControllerTest : IDisposable
    {
        private const string MockTenantId = "mocktenant";

        private readonly Mock<DeviceGroupsConverter> mockConverter;
        private readonly Mock<IKeyGenerator> mockGenerator;
        private readonly Mock<ILogger<DeviceGroupsController>> mockLogger;
        private readonly DeviceGroupsController controller;
        private readonly Random rand = new Random();
        private IDictionary<object, object> contextItems;
        private bool disposedValue = false;

        public DeviceGroupsControllerTest()
        {
            var mockIotHubManagerClient = new Mock<IIotHubManagerClient>();
            var mockBlobClient = new Mock<IBlobStorageClient>();
            var mockStorageAdapter = new Mock<IStorageAdapterClient>();
            var mockConvertLogger = new Mock<ILogger<DeviceGroupsConverter>>();

            this.mockConverter = new Mock<DeviceGroupsConverter>(
                mockIotHubManagerClient.Object,
                mockBlobClient.Object,
                mockStorageAdapter.Object,
                mockConvertLogger.Object);

            this.mockGenerator = new Mock<IKeyGenerator>();

            this.mockLogger = new Mock<ILogger<DeviceGroupsController>>();

            var mockHttpContext = new Mock<HttpContext> { DefaultValue = DefaultValue.Mock };
            var mockHttpRequest = new Mock<HttpRequest> { DefaultValue = DefaultValue.Mock };
            mockHttpRequest.Setup(m => m.HttpContext).Returns(mockHttpContext.Object);
            mockHttpContext.Setup(m => m.Request).Returns(mockHttpRequest.Object);

            this.controller = new DeviceGroupsController(this.mockConverter.Object, this.mockGenerator.Object, this.mockLogger.Object)
            {
                ControllerContext = new ControllerContext()
                {
                    HttpContext = mockHttpContext.Object,
                },
            };
            this.contextItems = new Dictionary<object, object>
            {
                { RequestExtension.ContextKeyTenantId, MockTenantId },
            };
            mockHttpContext.Setup(m => m.Items).Returns(this.contextItems);
        }

        [Fact]
        public void BeginDeviceGroupConversionTest()
        {
            string operationId = this.rand.NextString();

            this.mockGenerator
                .Setup(x => x.Generate())
                .Returns(operationId);

            this.mockConverter
                .Setup(x => x.ConvertAsync(
                    It.Is<string>(s => s == MockTenantId),
                    It.Is<string>(s => s == operationId)))
                .ReturnsAsync(
                    new ConversionApiModel
                    {
                        OperationId = operationId,
                    });

            var response = this.controller.BeginDeviceGroupConversion();

            this.mockGenerator
                .Verify(
                    x => x.Generate(),
                    Times.Once);

            this.mockConverter
                .Verify(
                    x => x.ConvertAsync(
                        It.Is<string>(s => s == MockTenantId),
                        It.Is<string>(s => s == operationId)),
                    Times.Once);

            Assert.Equal(MockTenantId, response.TenantId);
            Assert.Equal(operationId, response.OperationId);
        }

        [Fact]
        public void BeginDeviceGroupConversionReturnsOnBackgroundExceptionTest()
        {
            string operationId = this.rand.NextString();
            string exceptionMessage = this.rand.NextString();
            Exception convertException = new Exception(exceptionMessage);

            this.mockGenerator
                .Setup(x => x.Generate())
                .Returns(operationId);

            this.mockConverter
                .Setup(x => x.ConvertAsync(
                    It.Is<string>(s => s == MockTenantId),
                    It.Is<string>(s => s == operationId)))
                .ThrowsAsync(convertException);

            var response = this.controller.BeginDeviceGroupConversion();
            Thread.Sleep(5000);  // Sleep to allow the ConvertAsync to be called in backgorund

            this.mockConverter
                .Verify(
                    x => x.ConvertAsync(
                        It.Is<string>(s => s == MockTenantId),
                        It.Is<string>(s => s == operationId)),
                    Times.Once);

            Assert.Equal(MockTenantId, response.TenantId);
            Assert.Equal(operationId, response.OperationId);
        }

        public void Dispose()
        {
            this.Dispose(true);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!this.disposedValue)
            {
                if (disposing)
                {
                    this.controller.Dispose();
                }

                this.disposedValue = true;
            }
        }
    }
}