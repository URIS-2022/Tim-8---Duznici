﻿using System;
using System.Net;
using Blockcore.Features.WalletWatchOnly.Api.Controllers;
using Blockcore.Features.WalletWatchOnly.Interfaces;
using Blockcore.Features.WalletWatchOnly.Models;
using Blockcore.Utilities.JsonErrors;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace Blockcore.Features.WalletWatchOnly.Tests
{
    public class WatchOnlyWalletControllerTest
    {
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void Given_AddressIsNullOrEmpty_When_WatchIsCalled_Then_BadRequestIsReturned(string address)
        {
            var mockWalletManager = new Mock<IWatchOnlyWalletManager>();
            var controller = new WatchOnlyWalletController(mockWalletManager.Object);

            IActionResult result = controller.Watch(address);
            var errorResult = Assert.IsType<ErrorResult>(result);
            var errorResponse = Assert.IsType<ErrorResponse>(errorResult.Value);
            Assert.Single(errorResponse.Errors);
            Assert.NotNull(errorResult.StatusCode);
            Assert.Equal((int)HttpStatusCode.BadRequest, errorResult.StatusCode.Value);
        }

        [Fact]
        public void Given_ExceptionIsThrown_When_WatchIsCalled_Then_HttpConflictIsReturned()
        {
            string address = "non-null-or-empty-address";
            var mockWalletManager = new Mock<IWatchOnlyWalletManager>();
            mockWalletManager.Setup(wallet => wallet.WatchAddress(It.IsAny<string>())).Throws(new Exception());

            var controller = new WatchOnlyWalletController(mockWalletManager.Object);

            IActionResult result = controller.Watch(address);
            var errorResult = Assert.IsType<ErrorResult>(result);
            var errorResponse = Assert.IsType<ErrorResponse>(errorResult.Value);
            Assert.Single(errorResponse.Errors);
            Assert.NotNull(errorResult.StatusCode);
            Assert.Equal((int)HttpStatusCode.Conflict, errorResult.StatusCode.Value);
        }

        [Fact]
        public void Given_NoExceptionIsThrown_When_WatchIsCalled_Then_HttpOkIsReturned()
        {
            string address = "non-null-or-empty-address";
            var mockWalletManager = new Mock<IWatchOnlyWalletManager>();
            mockWalletManager.Setup(wallet => wallet.WatchAddress(It.IsAny<string>()));

            var controller = new WatchOnlyWalletController(mockWalletManager.Object);

            IActionResult result = controller.Watch(address);
            Assert.NotNull(result);
            Assert.IsType<OkResult>(result);
        }

        [Fact]
        public void Given_ExceptionIsThrown_When_GetWatchOnlyWalletIsCalled_Then_HttpBadRequestIsReturned()
        {
            var mockWalletManager = new Mock<IWatchOnlyWalletManager>();
            mockWalletManager.Setup(wallet => wallet.GetWatchOnlyWallet()).Throws(new Exception());

            var controller = new WatchOnlyWalletController(mockWalletManager.Object);

            IActionResult result = controller.GetWatchOnlyWallet();
            var errorResult = Assert.IsType<ErrorResult>(result);
            var errorResponse = Assert.IsType<ErrorResponse>(errorResult.Value);
            Assert.Single(errorResponse.Errors);
            Assert.NotNull(errorResult.StatusCode);
            Assert.Equal((int)HttpStatusCode.BadRequest, errorResult.StatusCode.Value);
        }

        [Fact]
        public void Given_NoExceptionIsThrown_When_GetWatchOnlyWalletIsCalled_Then_WatchOnlyWalletModelIsReturned()
        {
            var mockWalletManager = new Mock<IWatchOnlyWalletManager>();
            mockWalletManager.Setup(wallet => wallet.GetWatchOnlyWallet()).Returns(new WatchOnlyWallet());

            var controller = new WatchOnlyWalletController(mockWalletManager.Object);

            IActionResult result = controller.GetWatchOnlyWallet();

            mockWalletManager.VerifyAll();
            Assert.NotNull(result);
            var viewResult = Assert.IsType<JsonResult>(result);
            var resultValue = Assert.IsType<WatchOnlyWalletModel>(viewResult.Value);
            Assert.NotNull(resultValue);
        }
    }
}