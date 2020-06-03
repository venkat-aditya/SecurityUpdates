// <copyright file="SystemAdminController.cs" company="3M">
// Copyright (c) 3M. All rights reserved.
// </copyright>

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Mmm.Iot.Common.Services.Filters;
using Mmm.Iot.IdentityGateway.Services;
using Mmm.Iot.IdentityGateway.Services.Models;

namespace Mmm.Iot.IdentityGateway.WebService.Controllers
{
    [Route("v1/systemAdmin")]
    [TypeFilter(typeof(ExceptionsFilterAttribute))]
    public class SystemAdminController : Controller
    {
        private SystemAdminContainer container;
        private UserTenantContainer userTenantcontainer;

        public SystemAdminController(SystemAdminContainer container, UserTenantContainer userTenantcontainer)
        {
            this.container = container;
            this.userTenantcontainer = userTenantcontainer;
        }

        [HttpPost]
        [SystemAdminAuthorize]
        public async Task<SystemAdminModel> CreateSystemAdminAsync([FromBody] UserModel model)
        {
            SystemAdminInput systemAdminInput = new SystemAdminInput
            {
                UserId = model.UserId,
                Name = model.Name,
            };

            return await this.container.CreateAsync(systemAdminInput);
        }

        [HttpGet("getAllNonSystemAdmins")]
        [SystemAdminAuthorize]
        public async Task<UserListModel> GetAllNonSystemAdminsAsync()
        {
            var allUsers = await this.userTenantcontainer.GetAllUsersAsync();

            var existingSystemAdmins = await this.container.GetAllAsync();

            if (existingSystemAdmins != null && existingSystemAdmins.Models != null && existingSystemAdmins.Models.Count() > 0)
            {
                var existingSystemAdminIds = existingSystemAdmins.Models.Select(s => s.PartitionKey);

                allUsers.Models.RemoveAll(s => existingSystemAdminIds.Contains(s.UserId));
            }

            return allUsers;
        }

        [HttpGet("getAllSystemAdmins")]
        [SystemAdminAuthorize]
        public async Task<UserListModel> GetAllSystemAdminsAsync()
        {
            var existingSystemAdmins = await this.container.GetAllAsync();
            UserListModel usersList = new UserListModel();
            usersList.Models = new List<UserModel>();
            if (existingSystemAdmins.Models != null)
            {
                foreach (var systemAdmin in existingSystemAdmins.Models)
                {
                    usersList.Models.Add(
                        new UserModel()
                        {
                            UserId = systemAdmin.PartitionKey,
                            Name = systemAdmin.Name,
                        });
                }
            }

            return usersList;
        }

        [HttpDelete("{userId}")]
        [SystemAdminAuthorize]
        public async Task<SystemAdminModel> DeleteSystemAdminAsync(string userId)
        {
            SystemAdminInput systemAdminInput = new SystemAdminInput
            {
                UserId = userId,
            };
            return await this.container.DeleteAsync(systemAdminInput);
        }
    }
}