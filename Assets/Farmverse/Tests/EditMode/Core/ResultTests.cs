#nullable enable
using System;
using Farmverse.Contracts;
using Farmverse.Core.Results;
using NUnit.Framework;

namespace Farmverse.Tests.Core
{
    [TestFixture]
    public sealed class ResultTests
    {
        [Test]
        public void OkHasNoErrorAndFailHasNoValue()
        {
            var ok = Result<int>.Ok(5);
            Assert.IsTrue(ok.IsOk);
            Assert.AreEqual(5, ok.Value);
            Assert.Throws<InvalidOperationException>(() => _ = ok.Error);

            var fail = Result<int>.Fail(ErrorCode.NameTaken);
            Assert.IsTrue(fail.IsFailure);
            Assert.AreEqual(ErrorCode.NameTaken, fail.Error.Code);
            Assert.Throws<InvalidOperationException>(() => _ = fail.Value);
            Assert.AreEqual(7, fail.ValueOr(7));
        }

        [Test]
        public void MapAndThenShortCircuitOnFailure()
        {
            var doubled = Result<int>.Ok(4).Map(v => v * 2).Then(v => Result<string>.Ok("n=" + v));
            Assert.AreEqual("n=8", doubled.Value);

            var calls = 0;
            var failed = Result<int>.Fail(ErrorCode.NotFound).Map(v => { calls++; return v; });
            Assert.AreEqual(0, calls);
            Assert.AreEqual(ErrorCode.NotFound, failed.Error.Code);
        }

        [Test]
        public void AppErrorCarriesParamsAndAMessageKey()
        {
            var error = AppError.Of(ErrorCode.ClientOutdated, ("minVersion", "2.0.0"), ("missing", "20"));
            Assert.AreEqual("20", error.Param("missing"));
            Assert.AreEqual(string.Empty, error.Param("absent"));
            Assert.AreEqual("error.CLIENT_OUTDATED", error.MessageKey);
            Assert.AreEqual("error.UNKNOWN", AppError.Of(ErrorCode.Unknown).MessageKey);
        }

        [Test]
        public void AppErrorFromServerBody()
        {
            var body = new ErrorBody { Code = ErrorCode.RateLimited, RetryAfterMs = 1500 };
            var error = AppError.FromBody(body);
            Assert.AreEqual(ErrorCode.RateLimited, error.Code);
            Assert.AreEqual(1500L, error.RetryAfterMs);
        }
    }
}
