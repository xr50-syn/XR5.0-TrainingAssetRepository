﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Policy;
using System.Text;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Linq;
using XR50TrainingAssetRepo.Models;

namespace XR50TrainingAssetRepo.Controllers
{
    [Route("/xr50/trainingAssetRepository/[controller]")]
    [ApiController]
    public class programManagementController : ControllerBase
    {
        private readonly XR50TrainingAssetRepoContext _context;
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        public programManagementController(XR50TrainingAssetRepoContext context, HttpClient httpClient, IConfiguration configuration)
        {
            _context = context;
            _httpClient = httpClient;
            _configuration = configuration; 
        }

        // GET: api/TrainingProgram
        [HttpGet]
        public async Task<ActionResult<IEnumerable<TrainingProgram>>> GetTrainingProgram()
        {
            return await _context.TrainingPrograms.ToListAsync();
        }

        // GET: api/TrainingProgram/5
        [HttpGet("{tenantName}/{programName}")]
        public async Task<ActionResult<TrainingProgram>> GetTrainingProgram(string TenantName,string ProgramName)
        {
            var TrainingProgram = await _context.TrainingPrograms.FindAsync(TenantName,ProgramName);

            if (TrainingProgram == null)
            {
                return NotFound();
            }
            return TrainingProgram;
        }

        // PUT: api/TrainingProgram/5 
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
       /* [HttpPut("{TenantName}/{ProgramName}")]
        public async Task<IActionResult> PutTrainingProgram(string ProgramName, TrainingProgram TrainingProgram)
        {
            if (!ProgramName.Equals(TrainingProgram.ProgramName))
            {
                return BadRequest();
            }

            _context.Entry(TrainingProgram).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!TrainingProgramExists(ProgramName))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }

            return NoContent();
        }
*/
        
        // DELETE: api/TrainingProgram/5
        [HttpDelete("{tenantName}/{programName}")]
        public async Task<IActionResult> DeleteTrainingProgram(string TenantName,string ProgramName)
        {
            var TrainingProgram = await _context.TrainingPrograms.FindAsync(TenantName,ProgramName);
            if (TrainingProgram == null)
            {
                return NotFound();
            }
            var XR50Tenant = await _context.Tenants.FindAsync(TrainingProgram.TenantName);
            if (XR50Tenant == null)
            {
                return NotFound();
            }
            _context.TrainingPrograms.Remove(TrainingProgram);
            XR50Tenant.TrainingProgramList.Remove(TrainingProgram.ProgramName);
            await _context.SaveChangesAsync();
            
            return NoContent();
        }

        private bool TrainingProgramExists(string ProgramName)
        {
            return _context.TrainingPrograms.Any(e => e.ProgramName.Equals(ProgramName));
        }
    }
}
