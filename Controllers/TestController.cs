using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using XR50TrainingAssetRepo.Models;
using XR50TrainingAssetRepo.Services;
using Microsoft.AspNetCore.Authorization;

[ApiController]
[Route("api/[controller]")]
[AllowAnonymous]
[ApiExplorerSettings(GroupName = "test")]
public class TestController : ControllerBase
{
    [HttpGet]
    public ActionResult<string> Get()
    {
        return Ok("Test endpoint working!");
    }
}