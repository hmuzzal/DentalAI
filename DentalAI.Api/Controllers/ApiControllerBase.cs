using DentalAI.Api.Models;
using DentalAI.Application.Common.Models;
using Microsoft.AspNetCore.Mvc;

namespace DentalAI.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public abstract class ApiControllerBase : ControllerBase
{
    protected IActionResult HandleResult(Result result)
    {
        if (result.Success)
        {
            return Ok(new ApiResponse
            {
                Success = true,
                Message = result.Message
            });
        }

        return StatusCode(MapStatusCode(result.Status), new ApiResponse
        {
            Success = false,
            Message = result.Message,
            Errors = result.Errors
        });
    }

    protected IActionResult HandleResult<T>(Result<T> result)
    {
        if (result.Success)
        {
            return Ok(new ApiResponse
            {
                Success = true,
                Message = result.Message,
                Data = result.Data
            });
        }

        return StatusCode(MapStatusCode(result.Status), new ApiResponse
        {
            Success = false,
            Message = result.Message,
            Errors = result.Errors
        });
    }

    private static int MapStatusCode(ResultStatus status) => status switch
    {
        ResultStatus.ValidationError => StatusCodes.Status400BadRequest,
        ResultStatus.NotFound => StatusCodes.Status404NotFound,
        ResultStatus.Unauthorized => StatusCodes.Status401Unauthorized,
        ResultStatus.Forbidden => StatusCodes.Status403Forbidden,
        ResultStatus.Conflict => StatusCodes.Status409Conflict,
        ResultStatus.TooManyRequests => StatusCodes.Status429TooManyRequests,
        ResultStatus.Unavailable => StatusCodes.Status503ServiceUnavailable,
        _ => StatusCodes.Status400BadRequest
    };
}
