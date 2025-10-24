using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using AliantWebS.Models;
using AliantWebS;
using Microsoft.JSInterop;
using System.Runtime.CompilerServices;
//using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;

namespace AliantWebS.Data
{
    public class AppData
    {
        //ILocalStorageService localStorage;
        IJSRuntime js;
        AuthenticationStateProvider authenticationStateProvider;
        private readonly  SignInManager<ApplicationUser> signInManager;
   //    private readonly IHttpContextAccessor httpContextAccessor;

        public AppData(IJSRuntime js, AuthenticationStateProvider AuthenticationStateProvider)
        {
            //this.localStorage = localStorage;
            this.js = js;
            this.authenticationStateProvider = AuthenticationStateProvider;


        }

        public bool BIMInElementLayout = true;

        public bool Monitoring { get; set; } = false;



        public bool creating { get; set; } = false;
        public object obj1 { get; set; }
        public object obj2 { get; set; }
        public object obj3 { get; set; }


        private Element element;
        public Element Element
        {
            get => element ?? (element = new Element());
            set
            {
                element = value;
            }
        }


        private EditContext elementEditContext;
        public EditContext ElementEditContext
        {
            get => elementEditContext ?? (elementEditContext = new EditContext(Element));

        }


        private EditContext structureEditContext;
        public EditContext StructureEditContext
        {
            get => structureEditContext ?? (structureEditContext = new EditContext(Structure));

        }


        private Structure structure;
        public Structure Structure
        {
            get => structure;
            set
            {
                structure = value;

                string structureName = string.Empty;
                string displaySubmitButtonText = string.Empty;

                if (structure != null)
                {
                    structureName = "Structure: " + structure?.Name;
                    displaySubmitButtonText = "initial";

                }
                else
                {
                    structureName = string.Empty;
                    displaySubmitButtonText = "none";
                }


                DisplaySubmitButtonChanged?.Invoke(displaySubmitButtonText);
                StructureNameChanged?.Invoke(structureName);
            }
        }

        public event Action<string> StructureNameChanged;


        public Guid EventID { get; set; }


        //private Guid eventMpscenarioId;
        //public Guid EventMpscenarioId
        //{
        //    get => eventMpscenarioId;
        //    set
        //    {
        //        eventMpscenarioId = value;
        //        ScenarioChanged?.Invoke();

        //    }
        //}

        //public event Action ScenarioChanged;


        private string buttonText;
        public string ButtonText
        {
            get => buttonText;
            set
            {
                buttonText = value;
                ButtonTextChanged?.Invoke(buttonText);
               
            }
        }
        public event Action<string> ButtonTextChanged;

        private string formId;
        public string FormId
        {
            get => formId;
            set
            {
                formId = value;
                FormIdChanged?.Invoke(formId);
            }
        }
        public event Action<string> FormIdChanged;


        static public object network { get; set; }

        public async Task<string> GetRole()
        {
            var authState = await authenticationStateProvider.GetAuthenticationStateAsync();
            var user = authState.User;
            if (user.IsInRole("Administrator")) return "Administrator";
            if (user.IsInRole("Developer")) return "Developer";

            return "User";
        }

        public event Action OnChange;
        public void NSC()
        {


            OnChange?.Invoke();


        }

        public EventCallback OnRootEmployeesChango { get; set; }

        public event Action OnRootEmployeesChange;

        public void RootEmployeesChange()
        {


            OnRootEmployeesChange?.Invoke();


        }

        public IEnumerable<Object> checkedNodeObjects = Enumerable.Empty<Element>();

        public IEnumerable<Object> CheckedNodeObjects
        {
            get { return checkedNodeObjects; }   // get method
            set
            {

                //   if (checkedNodeObjects == value) return;
                checkedNodeObjects = value;

                OncheckedNodeObjectsChange?.Invoke(checkedNodeObjects.Select(x => ((Element)x)));
                //  OnChange?.Invoke();
            }
        }

        public event Action<IEnumerable<AliantWebS.Models.Element>> OncheckedNodeObjectsChange;


        public List<Element> appDataElements;

        private bool saveChanges = false;
        public bool SaveChanges
        {
            get => saveChanges;
            set
            {
                if (saveChanges == value) return;
                saveChanges = value;

                OnSaveChange.Invoke(saveChanges);
            }
        }

        public event Action<bool> OnSaveChange;

        private bool displaySubmitButton;
        public bool DisplaySubmitButton
        {
            get => displaySubmitButton;
            set
            {
                //  if (displaySubmitButton == value) return;

                displaySubmitButton = value;

                string s = string.Empty;

                if (displaySubmitButton == true)
                {
                    s = "initial";
                }
                else
                {
                    s = "none";
                }

                DisplaySubmitButtonChanged?.Invoke(s);
            }
        }


        public event Action<string> DisplaySubmitButtonChanged;


        bool? saveConfirmation;
        public bool SaveConfirmation
        {
            get
            {

                return (saveConfirmation ?? true);
            }
            set
            {

                if (saveConfirmation == value) return;
                saveConfirmation = value;

            }
        }

        static private bool _isAlarmActive;
        static public event Action OnAlarmStateChanged;

        static public bool IsAlarmActive
        {
            get => _isAlarmActive;
            set
            {
               // if (_isAlarmActive != value)
                {
                    _isAlarmActive = value;
                    NotifyAlarmStateChanged();
                }
            }
        }

        static private void NotifyAlarmStateChanged() => OnAlarmStateChanged?.Invoke();



        public async Task OnBeforeInternalNavigation(LocationChangingContext context)
        {
            if (SaveConfirmation)
            {
                if (SaveChanges)
                {
                    //This just displays the built -in browser confirm dialog, but you can display a custom prompt
                    // for internal navigations if you want.
                    var isConfirmed = await js.InvokeAsync<bool>("confirm", $"Data not saved. Are you sure you want to continue?");

                    if (!isConfirmed)
                    {
                        context.PreventNavigation();
                    }

                }
            }

        }

        public async Task<ApplicationUser> GetCurrentUserAsync()
        {
            var authState = await authenticationStateProvider.GetAuthenticationStateAsync();
            var userName = authState.User.Identity.Name;
            var user = await signInManager.UserManager.FindByNameAsync(userName);//???
            return user;
        }

        public async Task<bool> ValidateUserPasswordAsync(string password)
        {
            //var user = await signInManager.UserManager.FindByNameAsync(userName);
            var authState = await authenticationStateProvider.GetAuthenticationStateAsync();                     
            var userName = authState.User.Identity.Name;
            var user = await signInManager.UserManager.FindByNameAsync(userName);
            
            if (user != null)
            {
                var result = await signInManager.CheckPasswordSignInAsync(user, password, lockoutOnFailure: false);
                if (result == SignInResult.Success)
                {
                    return true;
                }
            }
            return false;


        }


    }
}

//List<Element> allElements = entitiesService.GetFilteredEntitiesAsync<Element>(a => (elementsIDs.Contains(a.ElementId)), "ElementType").Result;



//nodes = allElements.Where(a => (a.ElementType.Name ?? "") == "Node").ToList();
//elements = allElements.Where(a => a.ElementType.Name == "Beam2d").ToList();