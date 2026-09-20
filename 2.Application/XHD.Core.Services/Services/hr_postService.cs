using System;
using System.Collections.Generic;
using System.Text;
using System.Linq.Expressions;
using System.Threading.Tasks;

using XHD.Core.IServices;
using XHD.Core.IRepository;
using XHD.Core.Models;
using XHD.Core.Common;

using Newtonsoft;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Converters;

namespace XHD.Core.Services
{
    internal class hr_postService : BaseService<hr_post>, Ihr_postService
    {
        private readonly Ihr_postRepository _irepositoryBase;

        public hr_postService(Ihr_postRepository repository)
        {
            _irepository = repository;
            _irepositoryBase = repository;
        }

        /// <summary>
        /// Sprint 5 Wave 1 #34：service 层薄封装。
        /// </summary>
        public async Task<bool> UpdatePostEmpAsync(string postId, string empId, int? defaultPost)
        {
            if (string.IsNullOrWhiteSpace(postId))
            {
                return false;
            }

            return await _irepositoryBase.UpdatePostEmpAsync(postId, empId, defaultPost);
        }

        /// <summary>
        /// Sprint 5 Wave 1 #35：service 层薄封装。
        /// </summary>
        public async Task<bool> UpdatePostEmpbyEidAsync(string empId)
        {
            if (string.IsNullOrWhiteSpace(empId))
            {
                return false;
            }

            return await _irepositoryBase.UpdatePostEmpbyEidAsync(empId);
        }

        /// <summary>
        /// Sprint 5 Wave 1 #88：service 层薄封装。
        /// </summary>
        public async Task<List<hr_post>> GetPostByEmpIdAsync(string empId)
        {
            return await _irepositoryBase.GetPostByEmpIdAsync(empId);
        }

        /// <summary>
        /// Sprint 5 Wave 1 #89：service 层薄封装。
        /// </summary>
        public async Task<List<hr_post>> SerchAsync(string searchText)
        {
            return await _irepositoryBase.SerchAsync(searchText);
        }
    }
}
