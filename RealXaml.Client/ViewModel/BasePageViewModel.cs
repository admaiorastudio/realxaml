using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xamarin.Forms;

namespace AdMaiora.RealXaml.ViewModel
{
    public class BasePageViewModel : BaseViewModel
    {
        #region Constants and Fields

        private Page _context;

        #endregion

        #region Properties

        public INavigation Navigation
        {
            get
            {
                return _context.Navigation;
            }
        }

        public bool IsPageVisible
        {
            get
            {
                return this.Navigation.NavigationStack?.Last() == _context;
            }
        }

        #endregion

        #region Consturctors

        public BasePageViewModel(Page context)
        {
            _context = context;
        }

        #endregion

        #region Methods

        protected Task DisplayAlert(string title, string message, string cancel)
        {
            return _context.DisplayAlert(title, message, cancel);
        }

        protected Task<bool> DisplayAlert(string title, string message, string accept, string cancel)
        {
            return _context.DisplayAlert(title, message, accept, cancel);
        }

        #endregion
    }

}
