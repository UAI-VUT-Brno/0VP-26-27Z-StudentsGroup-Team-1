using System.Diagnostics;
using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using UAI.ZeroVP.API.Instance;
using UAI.ZeroVP.Data.Abstractions;
using UAI.ZeroVP.DTO;

namespace UAI.ZeroVP.API.Controllers;

/// <summary>
/// Diagnostický endpoint. Ve výuce Dockeru ukazuje, která instance
/// (kontejner) požadavek obsloužila a kolik úkolů je v úložišti.
/// </summary>
[ApiController]
[Route("api/info")]
[Produces("application/json")]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError, "application/problem+json",
    Description = "Neočekávaná chyba serveru.")]
public sealed class InfoController : ControllerBase
{
    private readonly ITodoRepository _repository;
    private readonly IHostEnvironment _environment;

    private readonly TimeProvider _timeProvider;
    private readonly InstanceIdentity _instance;

    /// <summary>Vytvoří kontroler se závislostmi z DI kontejneru.</summary>
    public InfoController(
        ITodoRepository repository,
        IHostEnvironment environment,
        TimeProvider timeProvider,
        InstanceIdentity instance)
    {
        _repository = repository;
        _environment = environment;
        _timeProvider = timeProvider;
        _instance = instance;
    }

    /// <summary>Vrátí informace o běžící instanci API.</summary>
       /// <response code="200">Popis instance včetně jejího jména a počtu úkolů v úložišti.</response>
    [HttpGet]
    [ProducesResponseType(typeof(InstanceInfoDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<InstanceInfoDto>> Get(CancellationToken cancellationToken)
    {
        var version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? "neznámá";

        // Process je IDisposable. Bez using by si každý požadavek na /api/info
        // podržel nativní handle procesu až do příštího sběru odpadků.
        using var process = Process.GetCurrentProcess();

        return Ok(new InstanceInfoDto
        {
            InstanceName = _instance.Name,
            MachineName = Environment.MachineName,
            Environment = _environment.EnvironmentName,
            Version = version,
            UtcNow = _timeProvider.GetUtcNow(),
            Uptime = _timeProvider.GetUtcNow() - process.StartTime.ToUniversalTime(),
            TodoCount = await _repository.CountAsync(cancellationToken)
        });
    }
}
