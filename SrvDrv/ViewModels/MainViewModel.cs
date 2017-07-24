﻿using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.ComponentModel.Composition.Hosting;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Input;
using Prism.Commands;
using Prism.Mvvm;
using Zodiacon.WPF;

namespace SrvDrv.ViewModels {
    [Export]
    class MainViewModel : BindableBase {
        IEnumerable<ServiceViewModel> _services;

        public IEnumerable<ServiceViewModel> Services {
            get {
                if(_services == null) {
                    _services = ServiceController.GetServices().Concat(ServiceController.GetDevices()).OrderBy(svc => svc.ServiceName)
                        .Select(svc => new ServiceViewModel(svc));
                }
                return _services;
            }
        }

        [Import]
        IUIServices UI;

        public MainViewModel() {
            StartCommand = new DelegateCommand(async () => {
                var vm = SelectedItem;
                await StartService(vm, true);
            }, () => SelectedItem != null && SelectedItem.Service.StartType != ServiceStartMode.Disabled && SelectedItem.Status == ServiceControllerStatus.Stopped)
            .ObservesProperty(() => SelectedItem);

            StopCommand = new DelegateCommand(async () => {
                var vm = SelectedItem;
                await StartService(vm, false);
            }, () => SelectedItem != null && SelectedItem.Service.CanStop && SelectedItem.Status == ServiceControllerStatus.Running)
            .ObservesProperty(() => SelectedItem);

            PauseContinueCommand = new DelegateCommand(async () => {
                var vm = SelectedItem;
                await Task.Run(() => PauseOrContinue(vm));
            }, () => SelectedItem != null && SelectedItem.Service.CanPauseAndContinue)
            .ObservesProperty(() => SelectedItem);

            GotoRegistryCommand = new DelegateCommand(() => {
                var regedit = Process.Start(Environment.GetFolderPath(Environment.SpecialFolder.Windows) + @"\regedit.exe");
                regedit.WaitForInputIdle();

                // now comes the tricky part. need to send keystrokes

            });

            OpenImageFolderCommand = new DelegateCommand(() => {
                var path = SelectedItem.ImagePath.Trim('\"');             
                if(path.StartsWith("\\SystemRoot\\"))
                    path = path.Replace("\\SystemRoot\\", "%SystemRoot%\\");
                Process.Start(Path.GetDirectoryName(Environment.ExpandEnvironmentVariables(path)));
            });
        }

        private bool PauseOrContinue(ServiceViewModel vm) {
            try {
                IsBusy = true;
                if(vm.Status == ServiceControllerStatus.Running) {
                    vm.Service.Pause();
                    vm.Service.WaitForStatus(ServiceControllerStatus.Paused, TimeSpan.FromSeconds(5));
                }
                else if(vm.Status == ServiceControllerStatus.Paused) {
                    vm.Service.Continue();
                    vm.Service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(5));
                }
                else
                    return false;
            }
            catch(System.ServiceProcess.TimeoutException) {
                return false;
            }
            finally {
                vm.Refresh();
                IsBusy = false;
            }
            return true;
        }

        private ServiceViewModel _selectedItem;

        public ServiceViewModel SelectedItem {
            get { return _selectedItem; }
            set { SetProperty(ref _selectedItem, value); }
        }

        public DelegateCommandBase StartCommand { get; }
        public DelegateCommandBase StopCommand { get; }
        public DelegateCommandBase GotoRegistryCommand { get; }
        public DelegateCommandBase PauseContinueCommand { get; }
        public DelegateCommandBase OpenImageFolderCommand { get; }

        private bool _isBusy;

        public bool IsBusy {
            get { return _isBusy; }
            set { SetProperty(ref _isBusy, value); }
        }


        private async Task StartService(ServiceViewModel service, bool start) {
            try {
                var svc = service.Service;
                IsBusy = true;
                if(start)
                    svc.Start();
                else
                    svc.Stop();
                await Task.Run(() => {
                    svc.WaitForStatus(start ? ServiceControllerStatus.Running : ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));
                });
            }
            catch(System.ServiceProcess.TimeoutException) {
                UI.MessageBoxService.ShowMessage("Operation timed out.", Constants.AppName);
            }
            catch(Exception ex) {
                UI.MessageBoxService.ShowMessage($"Error: {ex.Message}", Constants.AppName);
            }
            finally {
                IsBusy = false;

                service.Refresh();
                StartCommand.RaiseCanExecuteChanged();
                StopCommand.RaiseCanExecuteChanged();
            }
        }

        private bool _showServices = true;

        public bool ShowServices {
            get { return _showServices; }
            set {
                if(SetProperty(ref _showServices, value)) {
                    UpdateFilter();
                }
            }
        }

        private bool _showDrivers = true;

        public bool ShowDrivers {
            get { return _showDrivers; }
            set {
                if(SetProperty(ref _showDrivers, value)) {
                    UpdateFilter();
                }
            }
        }

        private string _searchText = string.Empty;

        public string SearchText {
            get { return _searchText; }
            set {
                if(SetProperty(ref _searchText, value)) {
                    UpdateFilter();
                }
            }
        }

        void UpdateFilter() {
            var view = CollectionViewSource.GetDefaultView(_services);
            view.Filter = obj => {
                var svc = (ServiceViewModel)obj;
                int type = (int)svc.Service.ServiceType;
                if(!ShowServices && type >= 0x10)
                    return false;

                if(!ShowDrivers && type < 0x10)
                    return false;

                var text = SearchText.ToLower();
                return svc.Name.ToLower().Contains(text) || svc.DisplayName.ToLower().Contains(text);
            };
        }
    }
}
