using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using BaseLibrary.DTOs;
using Microsoft.AspNetCore.Components.Authorization;

namespace ClientLibrary.Helpers
{
    public class CustomAuthStateProvider(LocalStorageService localStorageService) : AuthenticationStateProvider
    {
        private readonly ClaimsPrincipal _principal = new(new ClaimsIdentity());
        public override async Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            var stringToken = await localStorageService.GetToken();
            if (string.IsNullOrEmpty(stringToken))
            {
                return await Task.FromResult(new AuthenticationState(_principal));
            }

            var deserializeToken = Serializations.DeserializeJsonString<UserSession> (stringToken);
            if (deserializeToken is null)
            {
                return await Task.FromResult(new AuthenticationState(_principal));
            }

            var getUserClaims = DecryptToken(deserializeToken.Token!);
            if(getUserClaims is null)
            {
                return await Task.FromResult(new AuthenticationState(_principal));
            }

            var claimsPrincipal = SetClaimPrincipal(getUserClaims);
            return await Task.FromResult(new AuthenticationState(claimsPrincipal));
        }

        public async Task UpdateAuthenticationState(UserSession userSession)
        {
            var claimsPrincipal = new ClaimsPrincipal();
            if (userSession.Token is not null || userSession.RefreshToken is not null)
            {
                var serializeSession = Serializations.SerializeObj(userSession);
                await localStorageService.SetToken(serializeSession);
                var getUserClaims = DecryptToken(userSession.Token!);
                claimsPrincipal = SetClaimPrincipal(getUserClaims);
            }
            else
            {
                await localStorageService.RemoveToken();
            }
            NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(claimsPrincipal)));
        }

        private ClaimsPrincipal SetClaimPrincipal(CustomUserClaims userClaims)
        {
            if(userClaims.Email is null)
            {
                return new ClaimsPrincipal();
            }
            return new ClaimsPrincipal(new ClaimsIdentity(
                new List<Claim>
                {
                    new(ClaimTypes.NameIdentifier, userClaims.Id),
                    new(ClaimTypes.Name, userClaims.Name),
                    new(ClaimTypes.Email, userClaims.Email),
                    new(ClaimTypes.Role, userClaims.Role),
                }, "JwtAuth"));
        }

        private static CustomUserClaims DecryptToken(string jwtToken)
        {
            if (string.IsNullOrEmpty(jwtToken))
            {
                return new CustomUserClaims();
            }

            var handler = new JwtSecurityTokenHandler();
            var token = handler.ReadJwtToken(jwtToken);

            var id = token.Claims.FirstOrDefault(_ => _.Type == ClaimTypes.NameIdentifier);
            var name = token.Claims.FirstOrDefault(_ => _.Type == ClaimTypes.Name);
            var email = token.Claims.FirstOrDefault(_ => _.Type == ClaimTypes.Email);
            var role = token.Claims.FirstOrDefault(_ => _.Type == ClaimTypes.Role);

            return new CustomUserClaims(id!.Value, name!.Value, email!.Value, role!.Value);
        }
    }
}
