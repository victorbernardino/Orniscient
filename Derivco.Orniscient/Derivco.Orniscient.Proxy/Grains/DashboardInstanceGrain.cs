using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Derivco.Orniscient.Proxy.Filters;
using Derivco.Orniscient.Proxy.Grains.Filters;
using Derivco.Orniscient.Proxy.Grains.Models;
using Derivco.Orniscient.Proxy.Observers;
using Orleans;
using Orleans.Streams;

namespace Derivco.Orniscient.Proxy.Grains
{
    public class DashboardInstanceGrain : Grain, IDashboardInstanceGrain, IAsyncObserver<DiffModel>
    {
        private List<UpdateModel> CurrentStats { get; set; }
        private ObserverSubscriptionManager<IOrniscientObserver> _subsManager;
        private IDashboardCollectorGrain _dashboardCollectorGrain;
        private AppliedFilter _currentFilter;
        //private Logger _logger;

        public override async Task OnActivateAsync()
        {
            await base.OnActivateAsync();
            //_logger = GetLogger();

            _dashboardCollectorGrain = GrainFactory.GetGrain<IDashboardCollectorGrain>(Guid.Empty);
            _subsManager = new ObserverSubscriptionManager<IOrniscientObserver>();

            var streamProvider = GetStreamProvider(StreamKeys.StreamProvider);
            var stream = streamProvider.GetStream<DiffModel>(Guid.Empty, StreamKeys.OrniscientChanges);
            await stream.SubscribeAsync<DiffModel>(OnNextAsync);

            //_logger.Info("DashboardInstanceGrain Activated.");
        }

        public async Task<List<UpdateModel>> GetAll(AppliedFilter filter = null)
        {
            _currentFilter = filter;
            var allGrains = await _dashboardCollectorGrain.GetAll();
            return await ApplyFilter(allGrains);
        }

        public Task Subscribe(IOrniscientObserver observer)
        {
            _subsManager.Subscribe(observer);
            return TaskDone.Done;
        }

        public Task UnSubscribe(IOrniscientObserver observer)
        {
            _subsManager.Unsubscribe(observer);
            return TaskDone.Done;
        }

        public Task SetTypeFilter(GrainType[] types)
        {
            //TODO : Implement the SetTypeFilter on this level
            return TaskDone.Done;
        }

        public Task<GrainType[]> GetGrainTypes()
        {
            return _dashboardCollectorGrain.GetGrainTypes();
        }

        private async Task<List<UpdateModel>> ApplyFilter(List<UpdateModel> grains)
        {
            //_logger.Verbose("Filter Applied");
            if (_currentFilter == null)
                return grains;

            //order of filtering applies here.
            //1. Grain Id
            if (!string.IsNullOrEmpty(_currentFilter.GrainId))
            {
                return grains.Where(p => p.Guid.ToString().Contains(_currentFilter.GrainId)).ToList();
            }

            //2. Silo
            var grainQuery = grains.Where(p => _currentFilter.SelectedSilos == null || _currentFilter.SelectedSilos.Length == 0 || _currentFilter.SelectedSilos.Contains(p.Silo));


            var filterList = new Dictionary<string,List<string>>();

            var sourceGrainTypes = grains.Where(p=>_currentFilter.TypeFilters.Any(cf=>cf.TypeName==p.Type)).Select(p => p.Type).Distinct().ToList();
            foreach (var sourceGrainType in sourceGrainTypes)
            {
                var appliedTypeFilter = _currentFilter.TypeFilters.FirstOrDefault(p => p.TypeName == sourceGrainType);
                List<string> grainIdsGrainType = null;

                if (appliedTypeFilter?.SelectedValues != null && appliedTypeFilter.SelectedValues.Any())
                {
                    //fetch the filters
                    var filterGrain = GrainFactory.GetGrain<IFilterGrain>(Guid.Empty);
                    var currentTypeFilters =
                        await filterGrain.GetFilters(_currentFilter.TypeFilters.Select(p => p.TypeName).ToArray());

                    foreach (var currentTypeFilter in currentTypeFilters)
                    {
                        grainIdsGrainType = currentTypeFilter.Filters.
                            Where(
                                p =>
                                    appliedTypeFilter.SelectedValues.ContainsKey(p.FilterName) &&
                                    appliedTypeFilter.SelectedValues[p.FilterName].Contains(p.Value)
                            ).Select(p => p.GrainId).ToList();
                    }
                }
                filterList.Add(sourceGrainType, grainIdsGrainType);
            }

            grainQuery = grainQuery.Where(p =>filterList.ContainsKey(p.Type) && (filterList[p.Type] == null || filterList[p.Type].Contains(p.Guid.ToString())));
            return grainQuery.ToList();
        }

        public async Task OnNextAsync(DiffModel item, StreamSequenceToken token = null)
        {
            Debug.WriteLine($"OnNextAsync called with {item.NewGrains.Count} items");
            if (item.NewGrains != null && item.NewGrains.Any())
            {
                item.NewGrains = await ApplyFilter(item.NewGrains);
            }

            if (item.NewGrains != null && (item.NewGrains.Any() || item.RemovedGrains.Any()))
            {
                Debug.WriteLine($"Sending {item.NewGrains.Count} new grains to the observers..");
                _subsManager.Notify(s => s.GrainsUpdated(item));
            }
        }

        public Task OnCompletedAsync()
        {
            return TaskDone.Done;
        }

        public Task OnErrorAsync(Exception ex)
        {
            return TaskDone.Done;
        }
    }
}