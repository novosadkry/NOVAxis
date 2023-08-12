using Xunit;
using Moq;
using Moq.EntityFrameworkCore;

using Discord;
using NOVAxis.Core;
using NOVAxis.Database.Guild;
using NOVAxis.Preconditions;
using Microsoft.Extensions.DependencyInjection;

namespace NOVAxisTests.Preconditions
{
    public class RequireGuildRoleAttributeTests
    {
        private readonly Mock<IInteractionContext> _context;
        private readonly Mock<IGuild> _guild;
        private readonly ServiceProvider _services;

        private readonly List<GuildRole> _savedRoles;
        private readonly List<ulong> _userRoles;
        private readonly List<ulong> _guildRoles;

        public RequireGuildRoleAttributeTests()
        {
            _context = new Mock<IInteractionContext>();
            _guild = new Mock<IGuild>();

            _savedRoles = new List<GuildRole>();
            _userRoles = new List<ulong>();
            _guildRoles = new List<ulong>();

            var user = new Mock<IGuildUser>();
            var dbContext = new Mock<GuildDbContext>(new ProgramConfig());

            dbContext.Setup(x => x.Guilds)
                .ReturnsDbSet(new List<GuildInfo> { new() { Id = 0, Roles = _savedRoles }});

            _services = new ServiceCollection()
                .AddSingleton(dbContext.Object)
                .BuildServiceProvider();

            _guild.Setup(x => x.Id).Returns(0);
            _context.Setup(x => x.User).Returns(user.Object);
            _context.Setup(x => x.Guild).Returns(_guild.Object);

            user.Setup(x => x.Guild).Returns(_guild.Object);
            user.Setup(x => x.RoleIds).Returns(_userRoles);
        }

        private void SetupRoles()
        {
            foreach (var id in _guildRoles)
            {
                _guild.Setup(x => x.GetRole(id)).Returns(() =>
                {
                    var role = new Mock<IRole>();
                    role.Setup(x => x.Id).Returns(1);
                    return role.Object;
                });
            }
        }

        [Fact]
        public async Task UserIsInRole()
        {
            // Arrange
            var attribute = new RequireGuildRoleAttribute("DJ");
            _savedRoles.Add(new GuildRole { Name = "DJ", Id = 2 });
            _userRoles.AddRange(new List<ulong> { 2, 3 });
            _guildRoles.AddRange(new List<ulong> { 1, 2, 3 });
            SetupRoles();

            // Act
            var result = await attribute.CheckRequirementsAsync(_context.Object, null, _services);

            // Assert
            Assert.True(result.IsSuccess);
        }

        [Fact]
        public async Task UserIsNotInRole()
        {
            // Arrange
            var attribute = new RequireGuildRoleAttribute("DJ");
            _savedRoles.Add(new GuildRole { Name = "DJ", Id = 2 });
            _userRoles.AddRange(new List<ulong> { 1, 3 });
            _guildRoles.AddRange(new List<ulong> { 1, 2, 3 });
            SetupRoles();

            // Act
            var result = await attribute.CheckRequirementsAsync(_context.Object, null, _services);

            // Assert
            Assert.False(result.IsSuccess);
        }

        [Fact]
        public async Task RoleDoesNotExist()
        {
            // Arrange
            var attribute = new RequireGuildRoleAttribute("DJ");
            _savedRoles.Add(new GuildRole { Name = "DJ", Id = 2 });
            _userRoles.AddRange(new List<ulong> { 1, 3 });
            _guildRoles.AddRange(new List<ulong> { 1, 3 });
            SetupRoles();

            // Act
            var result = await attribute.CheckRequirementsAsync(_context.Object, null, _services);

            // Assert
            Assert.True(result.IsSuccess);
        }
    }
}
