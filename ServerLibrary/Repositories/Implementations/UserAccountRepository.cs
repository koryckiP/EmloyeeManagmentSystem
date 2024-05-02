using BaseLibrary.DTOs;
using BaseLibrary.Entities;
using BaseLibrary.Responses;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using ServerLibrary.Data;
using ServerLibrary.Helpers;
using ServerLibrary.Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace ServerLibrary.Repositories.Implementations
{
    public class UserAccountRepository : IUserAccount
    {
        private readonly AppDbContext _appDbContext;
        private readonly IOptions<JwtSection> _options;

        public UserAccountRepository(IOptions<JwtSection> options, AppDbContext appDbContext)
        {
            _options = options;
            _appDbContext = appDbContext;
        }

        public async Task<GeneralResponse> CreateAsync(Register user)
        {
            if (user is null) { return new GeneralResponse(false, "Model is null"); }

            var checkUserEmail = await FindUserByEmail(user.Email!);
            if (checkUserEmail is not null) { return new GeneralResponse(false, "Email already in use"); }

            var applicationUser = await AddToDatabase(new ApplicationUser()
            {
                FullName = user.FullName,
                Email = user.Email,
                Password = BCrypt.Net.BCrypt.HashPassword(user.Password)
            });

            var checkAdminRole = await _appDbContext.SystemRoles.FirstOrDefaultAsync(_ => _.Name!.Equals(Constants.Admin));
            if (checkAdminRole is null)
            {
                var createAdminRole = await AddToDatabase(new SystemRole()
                {
                    Name = Constants.Admin,
                });
                await AddToDatabase(new UserRole()
                {
                    RoleId = createAdminRole.Id,
                    UserId = applicationUser.Id
                });
                return new GeneralResponse(true, "Account registered");
            }

            var checkUserRole = await _appDbContext.SystemRoles.FirstOrDefaultAsync(_ => _.Name!.Equals(Constants.User));
            var response = new SystemRole();
            if (checkUserRole is null)
            {
                response = await AddToDatabase(new SystemRole()
                {
                    Name = Constants.User
                });
                await AddToDatabase(new UserRole()
                {
                    RoleId = response.Id,
                    UserId = applicationUser.Id
                });
            }
            else
            {
                await AddToDatabase(new UserRole()
                {
                    RoleId = checkUserRole.Id,
                    UserId = applicationUser.Id
                });
            }
            return new GeneralResponse(true, "Account created");


        }

        public async Task<LoginResponse> SignInAsync(Login user)
        {
            if (user is null) { return new LoginResponse(false, "Model is null"); }

            var checkUser = await FindUserByEmail(user.Email!);
            if (checkUser is null) { return new LoginResponse(false, "Email not found"); }

            if(!BCrypt.Net.BCrypt.Verify(user.Password, checkUser.Password))
            {
                return new LoginResponse(false, "Password incorrect");
            }

            var userRole = await FindUserRole(checkUser.Id);
            if (userRole is null)
            {
                return new LoginResponse(false, "user role not found");
            }
            var roleName = await FindRoleName(userRole.Id);
            if (roleName is null)
            {
                return new LoginResponse(false, "role name not found");
            }

            string jwtToken = GenerateToken(checkUser, roleName.Name);
            string refreshToken = GenerateRefreshToken();

            var userRefreshTokens = await _appDbContext.RefreshTokens.FirstOrDefaultAsync(_ => _.UserId.Equals(checkUser.Id));
            if (userRefreshTokens is not null)
            {
                userRefreshTokens!.Token = refreshToken;
                await _appDbContext.SaveChangesAsync();
            }
            else
            {
                await AddToDatabase(new RefreshTokenInfo()
                {
                    Token = refreshToken,
                    UserId = checkUser.Id
                });
            }

            return new LoginResponse(true,"Login succesfull", jwtToken, refreshToken);
        }



        private string GenerateToken(ApplicationUser user, string? role)
        {
            var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Value.Key!));
            var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);
            var userClaims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.FullName!),
                new Claim(ClaimTypes.Email, user.Email!),
                new Claim(ClaimTypes.Role, role!)    
            };
            var token = new JwtSecurityToken(
                issuer: _options.Value.Issuer,
                audience: _options.Value.Audience,
                claims: userClaims,
                expires: DateTime.Now.AddDays(1),
                signingCredentials: credentials
                );
            return new JwtSecurityTokenHandler().WriteToken(token);
        }
        private string GenerateRefreshToken()
        {
            return Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        } 
        private async Task<UserRole> FindUserRole(int userId)
        {
            return await _appDbContext.UserRoles.FirstOrDefaultAsync(_ => _.UserId == userId);
        }
        private async Task<SystemRole> FindRoleName(int roleId)
        {
            return await _appDbContext.SystemRoles.FirstOrDefaultAsync(_ => _.Id == roleId);
        }
        public async Task<LoginResponse> RefreshTokenAsync(RefreshToken token)
        {
            if (token == null)
            {
                return new LoginResponse(false, "Null request");
            }
            var findToken = await _appDbContext.RefreshTokens.FirstOrDefaultAsync(_ => _.Token!.Equals(token.Token));
            if (findToken is null)
            {
                return new LoginResponse(false, "Refresh token not found");
            }

            var user = await _appDbContext.ApplicationUsers.FirstOrDefaultAsync(_ => _.Id.Equals(findToken.UserId));
            if (user is null)
            {
                return new LoginResponse(false, "User not found");
            }

            var userRole = await FindUserRole(user.Id);
            var roleName = await FindRoleName(userRole.RoleId);
            string jwtToken = GenerateToken(user, roleName.Name);
            string newRefreshToken = GenerateRefreshToken();

            var refreshToken = await _appDbContext.RefreshTokens.FirstOrDefaultAsync(_ => _.UserId.Equals(user.Id));
            if (refreshToken is null)
            {
                return new LoginResponse(false, "Refresh token failed to generate");
            }

            refreshToken.Token = newRefreshToken;
            await _appDbContext.SaveChangesAsync();
            return new LoginResponse(true, "Token refreshed successfully", jwtToken, newRefreshToken);
        }
        private async Task<ApplicationUser> FindUserByEmail(string email)
        {
            return await _appDbContext.ApplicationUsers.FirstOrDefaultAsync(u => u.Email.ToLower().Equals(email.ToLower()));
        }

        private async Task<T> AddToDatabase<T>(T model)
        {
            var result = _appDbContext.Add(model!);
            await _appDbContext.SaveChangesAsync();
            return (T)result.Entity;
        }


    }
}
