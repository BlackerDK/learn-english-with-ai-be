using System;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace backend.Controllers
{
    [Authorize]
    [ApiController]
    public abstract class BaseAuthController : ControllerBase
    {
        protected Guid CurrentUserId
        {
            get
            {
                var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                return Guid.TryParse(claim, out var id) ? id : Guid.Empty;
            }
        }
    }
}
