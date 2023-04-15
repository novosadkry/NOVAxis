using Xunit;
using Moq;

using Discord;
using NOVAxis.Preconditions;

namespace NOVAxisTests.Preconditions
{
    public class CooldownAttributeTests
    {
        [Theory]
        [InlineData(5, 0_000)]
        [InlineData(5, 4_999)]
        [InlineData(600, 0_000)]
        [InlineData(600, 599_999)]
        public async Task UserTriggersCooldown(int cooldown, double millis)
        {
            // Arrange
            var context = new Mock<IInteractionContext>();
            var interaction = new Mock<IDiscordInteraction>();
            var user = new Mock<IUser>();
            var timestamp = DateTimeOffset.UtcNow;

            user.Setup(x => x.Id).Returns(1L);
            interaction.Setup(x => x.CreatedAt).Returns(timestamp);
            context.Setup(x => x.Interaction).Returns(interaction.Object);
            context.Setup(x => x.User).Returns(user.Object);

            var attribute = new CooldownAttribute(cooldown);

            // Act and assert
            var result = await attribute.CheckRequirementsAsync(context.Object, null, null);
            Assert.True(result.IsSuccess);

            // Arrange a second message
            interaction = new Mock<IDiscordInteraction>();
            interaction.Setup(x => x.CreatedAt).Returns(timestamp.AddMilliseconds(millis));
            context.Setup(x => x.Interaction).Returns(interaction.Object);

            // Act and assert
            result = await attribute.CheckRequirementsAsync(context.Object, null, null);
            Assert.False(result.IsSuccess);
        }

        [Theory]
        [InlineData(0, 0_000)]
        [InlineData(0, 0_999)]
        [InlineData(5, 5_000)]
        [InlineData(5, 5_999)]
        public async Task UserWaitsForCooldown(int cooldown, double millis)
        {
            // Arrange
            var context = new Mock<IInteractionContext>();
            var interaction = new Mock<IDiscordInteraction>();
            var user = new Mock<IUser>();
            var timestamp = DateTimeOffset.UtcNow;

            user.Setup(x => x.Id).Returns(1L);
            interaction.Setup(x => x.CreatedAt).Returns(timestamp);
            context.Setup(x => x.Interaction).Returns(interaction.Object);
            context.Setup(x => x.User).Returns(user.Object);

            var attribute = new CooldownAttribute(cooldown);

            // Act and assert
            var result = await attribute.CheckRequirementsAsync(context.Object, null, null);
            Assert.True(result.IsSuccess);

            // Arrange a second message
            interaction = new Mock<IDiscordInteraction>();
            interaction.Setup(x => x.CreatedAt).Returns(timestamp.AddMilliseconds(millis));
            context.Setup(x => x.Interaction).Returns(interaction.Object);

            // Act and assert
            result = await attribute.CheckRequirementsAsync(context.Object, null, null);
            Assert.True(result.IsSuccess);
        }

        [Fact]
        public async Task UserTriggersCooldownWithoutWarning()
        {
            // Arrange
            var context = new Mock<IInteractionContext>();
            var interaction = new Mock<IDiscordInteraction>();
            var user = new Mock<IUser>();
            var timestamp = DateTimeOffset.UtcNow;

            user.Setup(x => x.Id).Returns(1L);
            interaction.Setup(x => x.CreatedAt).Returns(timestamp);
            context.Setup(x => x.Interaction).Returns(interaction.Object);
            context.Setup(x => x.User).Returns(user.Object);

            var attribute = new CooldownAttribute(600);

            // Act and assert
            var result = await attribute.CheckRequirementsAsync(context.Object, null, null);
            Assert.True(result.IsSuccess);

            // Arrange a second message
            interaction = new Mock<IDiscordInteraction>();
            interaction.Setup(x => x.CreatedAt).Returns(timestamp);
            context.Setup(x => x.Interaction).Returns(interaction.Object);

            // Act and assert
            result = await attribute.CheckRequirementsAsync(context.Object, null, null);
            Assert.True(!result.IsSuccess && result.ErrorReason == "User has command on cooldown");

            // Arrange a third message
            interaction = new Mock<IDiscordInteraction>();
            interaction.Setup(x => x.CreatedAt).Returns(timestamp);
            context.Setup(x => x.Interaction).Returns(interaction.Object);

            // Act and assert
            result = await attribute.CheckRequirementsAsync(context.Object, null, null);
            Assert.True(!result.IsSuccess && result.ErrorReason == "User has command on cooldown (no warning)");
        }
    }
}
