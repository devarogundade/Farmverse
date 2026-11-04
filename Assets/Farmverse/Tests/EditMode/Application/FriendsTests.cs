#nullable enable
using Farmverse.Application.Features;
using NUnit.Framework;

namespace Farmverse.Tests.Application
{
    [TestFixture]
    public sealed class FriendsTests
    {
        [Test]
        public void Invite_codes_are_read_from_what_the_player_types_or_pastes()
        {
            Assert.AreEqual("ABCD2345", FriendsService.CodeFrom("ABCD2345"));
            Assert.AreEqual("ABCD2345", FriendsService.CodeFrom(" abcd 2345 "));
            Assert.AreEqual("ABCD2345", FriendsService.CodeFrom("ABCD-2345"));
            Assert.AreEqual("ABCD2345", FriendsService.CodeFrom("https://farmverse.world/invite/ABCD2345"));
            Assert.AreEqual("ABCD2345", FriendsService.CodeFrom("Join me! https://farmverse.world/invite/abcd2345?utm=x"));
            Assert.IsNull(FriendsService.CodeFrom("ABC"));
            Assert.IsNull(FriendsService.CodeFrom("ABCD23456"));
            Assert.IsNull(FriendsService.CodeFrom("ÄBCD2345"));
            Assert.IsNull(FriendsService.CodeFrom(""));
        }
    }
}
