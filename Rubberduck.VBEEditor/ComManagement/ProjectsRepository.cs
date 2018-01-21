﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Rubberduck.VBEditor.SafeComWrappers.Abstract;
using Rubberduck.VBEditor.Extensions;
using Rubberduck.VBEditor.SafeComWrappers;

namespace Rubberduck.VBEditor.ComManagement
{
    public class ProjectsRepository : IProjectsRepository
    {
        private IVBProjects _projectsCollection;
        private readonly IDictionary<string, IVBProject> _projects = new Dictionary<string, IVBProject>();
        private readonly IDictionary<string, IVBComponents> _componentsCollections = new Dictionary<string, IVBComponents>();
        private readonly IDictionary<QualifiedModuleName, IVBComponent> _components = new Dictionary<QualifiedModuleName, IVBComponent>();
        private readonly IDictionary<QualifiedModuleName, ICodeModule> _codeModules = new Dictionary<QualifiedModuleName, ICodeModule>();

        private readonly ReaderWriterLockSlim _refreshProtectionLock = new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion);

        public ProjectsRepository(IVBE vbe)
        {
            _projectsCollection = vbe.VBProjects;
        }

        private void LoadCollections()
        {
            LoadProjects();
            LoadComponentsCollections();
            LoadComponents();
        }

        private void LoadProjects()
        {
            foreach (var project in _projectsCollection)
            {
                if (project.Protection == ProjectProtection.Locked)
                {
                    project.Dispose();
                    continue;
                }

                EnsureValidProjectId(project);
                _projects.Add(project.ProjectId, project);
            }
        }

        private void EnsureValidProjectId(IVBProject project)
        {
            if (string.IsNullOrEmpty(project.ProjectId) || _projects.Keys.Contains(project.ProjectId))
            {
                project.AssignProjectId();
            }
        }

        private void LoadComponentsCollections()
        {
            foreach (var (projectId, project) in _projects)
            {
                _componentsCollections.Add(projectId, project.VBComponents);
            }
        }

        private void LoadComponents()
        {
            foreach (var components in _componentsCollections.Values)
            {
                LoadComponents(components);
            }
        }

        private void LoadComponents(IVBComponents componentsCollection)
        {
            foreach (var component in componentsCollection)
            {
                var qmn = component.QualifiedModuleName;
                _components.Add(qmn, component);
                _codeModules.Add(qmn, component.CodeModule);
            }
        }

        public void Refresh()
        {
            ExecuteWithinWriteLock(() => RefreshCollections());
        }

        private void ExecuteWithinWriteLock(Action action)
        {
            if (_disposed)
            {
                return; //The lock has already been diposed.
            }

            var writeLockTaken = false;
            try
            {
                _refreshProtectionLock.EnterWriteLock();
                writeLockTaken = true;
                action.Invoke();
            }
            finally
            {
                if (writeLockTaken)
                {
                    _refreshProtectionLock.ExitWriteLock();
                }
            }
        }

        private void RefreshCollections()
        {
            //We save a copy of the collections and only refresh after the collections have been loaded again
            //to avoid disconnecting any RCWs from the underlying COM object for objects that still exist.
            var projects = ClearComWrapperDictionary(_projects);
            var componentCollections = ClearComWrapperDictionary(_componentsCollections);
            var components = ClearComWrapperDictionary(_components);
            var codeModules = ClearComWrapperDictionary(_codeModules);

            LoadCollections();

            DisposeWrapperEnumerable(projects);
            DisposeWrapperEnumerable(componentCollections);
            DisposeWrapperEnumerable(components);
            DisposeWrapperEnumerable(codeModules);
        }

        private IEnumerable<TWrapper> ClearComWrapperDictionary<TKey, TWrapper>(IDictionary<TKey, TWrapper> dictionary)
            where TWrapper : ISafeComWrapper
        {
            var copy = dictionary.Values.ToList();
            dictionary.Clear();
            return copy;
        }

        private void DisposeWrapperEnumerable<TWrapper>(IEnumerable<TWrapper> wrappers) where TWrapper : ISafeComWrapper
        {
            foreach (var wrapper in wrappers)
            {
                wrapper.Dispose();
            }
        }

        private void RefreshCollections(string projectId)
        {
            IVBProject project;
            if (!_projects.TryGetValue(projectId, out project))
            {
                return;
            }

            var componentsCollection = _componentsCollections[projectId];
            var components = _components.Where(kvp => kvp.Key.ProjectId.Equals(projectId)).ToList();
            var codeModules = _codeModules.Where(kvp => kvp.Key.ProjectId.Equals(projectId)).ToList();

            foreach (var qmn in components.Select(kvp => kvp.Key))
            {
                _components.Remove(qmn);
                _codeModules.Remove(qmn);
            }

            _componentsCollections[projectId] = project.VBComponents;
            LoadComponents(_componentsCollections[projectId]);

            componentsCollection.Dispose();
            DisposeWrapperEnumerable(components.Select(kvp => kvp.Value));
            DisposeWrapperEnumerable(codeModules.Select(kvp => kvp.Value));
        }

        public void Refresh(string projectId)
        {
            ExecuteWithinWriteLock(() => RefreshCollections(projectId));
        }

        public IVBProjects ProjectsCollection()
        {
            return _projectsCollection;
        }

        public IEnumerable<(string ProjectId, IVBProject Project)> Projects()
        {
            return EvaluateWithinReadLock(() => _projects.Select(kvp => (kvp.Key, kvp.Value)).ToList()) ?? new List<(string, IVBProject)>();
        }

        private T EvaluateWithinReadLock<T>(Func<T> function) where T: class
        {
            if (_disposed)
            {
                return default(T); //The lock has already been diposed.
            }

            var readLockTaken = false;
            try
            {
                _refreshProtectionLock.EnterReadLock();
                readLockTaken = true;
                return function.Invoke();
            }
            finally
            {
                if (readLockTaken)
                {
                    _refreshProtectionLock.ExitReadLock();
                }
            }
        }

        public IVBProject Project(string projectId)
        {
            if (projectId == null)
            {
                return null;
            }

            return EvaluateWithinReadLock(() => _projects.TryGetValue(projectId, out var project) ? project : null);
        }

        public IVBComponents ComponentsCollection(string projectId)
        {
            if (projectId == null)
            {
                return null;
            }

            return EvaluateWithinReadLock(() => _componentsCollections.TryGetValue(projectId, out var componenstCollection) ? componenstCollection : null);
        }

        public IEnumerable<(QualifiedModuleName QualifiedModuleName, IVBComponent Component)> Components()
        {
            return EvaluateWithinReadLock(() => _components.Select(kvp => (kvp.Key, kvp.Value)).ToList()) ?? new List<(QualifiedModuleName, IVBComponent)>();
        }

        public IEnumerable<(QualifiedModuleName QualifiedModuleName, IVBComponent Component)> Components(string projectId)
        {
            return EvaluateWithinReadLock(() => _components.Where(kvp => kvp.Key.ProjectId.Equals(projectId))
                       .Select(kvp => (kvp.Key, kvp.Value))
                       .ToList())
                   ?? new List<(QualifiedModuleName, IVBComponent)>();
        }

        public IVBComponent Component(QualifiedModuleName qualifiedModuleName)
        {
            return EvaluateWithinReadLock(() => _components.TryGetValue(qualifiedModuleName, out var component) ? component : null);
        }

        public IEnumerable<(QualifiedModuleName QualifiedModuleName, ICodeModule CodeModule)> CodeModules()
        {
            return EvaluateWithinReadLock(() => _codeModules.Select(kvp => (kvp.Key, kvp.Value)).ToList()) ?? new List<(QualifiedModuleName, ICodeModule)>();
        }

        public IEnumerable<(QualifiedModuleName QualifiedModuleName, ICodeModule CodeModule)> CodeModules(string projectId)
        {
            return EvaluateWithinReadLock(() => _codeModules.Where(kvp => kvp.Key.ProjectId.Equals(projectId))
                       .Select(kvp => (kvp.Key, kvp.Value))
                       .ToList())
                   ?? new List<(QualifiedModuleName, ICodeModule)>();
        }

        public ICodeModule CodeModule(QualifiedModuleName qualifiedModuleName)
        {
            return EvaluateWithinReadLock(() => _codeModules.TryGetValue(qualifiedModuleName, out var codeModule) ? codeModule : null);
        }

        private bool _disposed;
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            
            ExecuteWithinWriteLock(() => ClearCollections());

            _disposed = true;

            _projectsCollection.Dispose();
            _projectsCollection = null;

            _refreshProtectionLock.Dispose();
        }

        private void ClearCollections()
        {
            var projects = ClearComWrapperDictionary(_projects);
            var componentCollections = ClearComWrapperDictionary(_componentsCollections);
            var components = ClearComWrapperDictionary(_components);
            var codeModules = ClearComWrapperDictionary(_codeModules);

            DisposeWrapperEnumerable(projects);
            DisposeWrapperEnumerable(componentCollections);
            DisposeWrapperEnumerable(components);
            DisposeWrapperEnumerable(codeModules);
        }
    }
}
