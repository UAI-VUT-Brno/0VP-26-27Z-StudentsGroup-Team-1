using System.Diagnostics;
using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using UAI.ZeroVP.Data.Abstractions;
using UAI.ZeroVP.DTO;

namespace UAI.ZeroVP.API.Controllers;

/// <summary>
/// Diagnostický endpoint. Ve výuce Dockeru ukazuje, která instance
/// (kontejner) požadavek obsloužila a kolik dat má právě ve své paměti.
/// </summary>
[ApiController]
[Route("api/info")]
[Produces("application/json")]
public sealed class InfoController : ControllerBase
{
    private readonly ITodoRepository _repository;
    private readonly IHostEnvironment _environment;
    private readonly TimeProvider _timeProvider;

    /// <summary>Vytvoří kontroler se závislostmi z DI kontejneru.</summary>
    public InfoController(ITodoRepository repository, IHostEnvironment environment, TimeProvider timeProvider)
    {
        _repository = repository;
        _environment = environment;
        _timeProvider = timeProvider;
    }

    /// <summary>Vrátí informace o běžící instanci API.</summary>
    /// <response code="200">Popis instance včetně počtu úkolů v paměti.</response>
    [HttpGet]
    [ProducesResponseType(typeof(InstanceInfoDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<InstanceInfoDto>> Get(CancellationToken cancellationToken)
    {
        var version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? "neznámá";

        return Ok(new InstanceInfoDto
        {
            MachineName = Environment.MachineName,
            Environment = _environment.EnvironmentName,
            Version = version,
            UtcNow = _timeProvider.GetUtcNow(),
            Uptime = _timeProvider.GetUtcNow() - Process.GetCurrentProcess().StartTime.ToUniversalTime(),
            TodoCount = await _repository.CountAsync(cancellationToken)
        });
    }
}
