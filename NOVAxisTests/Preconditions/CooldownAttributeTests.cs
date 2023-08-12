using Xunit;
using Moq;

using Discord;
using NOVAxis.Preconditions;

namespace NOVAxisTests.Preconditions
{
    public class CooldownAttributeTests
    {
        private readonly Mock<IInteractionContext> _context;
        private readonly Mock<IUser> _user;
        private readonly DateTimeOffset _now;
        private IDiscordInteraction _interaction;

        public CooldownAttributeTests()
        {
            _context = new Mock<IInteractionContext>();
            _user = new Mock<IUser>();
            _now = DateTimeOffset.UtcNow;

            _user.Setup(x => x.Id).Returns(1L);
            _context.Setup(x => x.Interaction).Returns(() => _interaction);
            _context.Setup(x => x.User).Returns(_user.Object);
        }

        private static IDiscordInteraction CreateInteraction(DateTimeOffset timeStamp)
        {
            var mock = new Mock<IDiscordInteraction>();
            mock.Setup(x => x.CreatedAt).Returns(timeStamp);
            return mock.Object;
        }

        [Theory]
        [InlineData(5, 0_000)]
        [InlineData(5, 4_999)]
        [InlineData(600, 0_000)]
        [InlineData(600, 599_999)]
        public async Task UserTriggersCooldown(int cooldown, double millis)
        {
            // Arrange first message
            var attribute = new CooldownAttribute(cooldown);
            _interaction = CreateInteraction(_now);

            // Act and assert
            var result = await attribute.CheckRequirementsAsync(_context.Object, null, null);
            Assert.True(result.IsSuccess);

            // Arrange second message
            _interaction = CreateInteraction(_now.AddMilliseconds(millis));
                
            // Act and assert
            result = await attribute.CheckRequirementsAsync(_context.Object, null, null);
            Assert.False(result.IsSuccess);
        }

        [Theory]
        [InlineData(0, 0_000)]
        [InlineData(0, 0_999)]
        [InlineData(5, 5_000)]
        [InlineData(5, 5_999)]
        public async Task UserWaitsForCooldown(int cooldown, double millis)
        {
            // Arrange first message
            var attribute = new CooldownAttribute(cooldown);
            _interaction = CreateInteraction(_now);

            // Act and assert
            var result = await attribute.CheckRequirementsAsync(_context.Object, null, null);
            Assert.True(result.IsSuccess);

            // Arrange second message
            _interaction = CreateInteraction(_now.AddMilliseconds(millis));

            // Act and assert
            result = await attribute.CheckRequirementsAsync(_context.Object, null, null);
            Assert.True(result.IsSuccess);
        }

        [Fact]
        public async Task UserTriggersCooldownWithoutWarning()
        {
            // Arrange first message
            var attribute = new CooldownAttribute(600);
            _interaction = CreateInteraction(_now);

            // Act and assert
            var result = await attribute.CheckRequirementsAsync(_context.Object, null, null);
            Assert.True(result.IsSuccess);

            // Arrange second message
            _interaction = CreateInteraction(_now);

            // Act and assert
            result = await attribute.CheckRequirementsAsync(_context.Object, null, null);
            Assert.True(!result.IsSuccess && result.ErrorReason == "User has command on cooldown");

            // Arrange third message
            _interaction = CreateInteraction(_now);

            // Act and assert
            result = await attribute.CheckRequirementsAsync(_context.Object, null, null);
            Assert.True(!result.IsSuccess && result.ErrorReason == "User has command on cooldown (no warning)");
        }
    }
}
