# RealXaml
RealXaml is a live viewer and hot reloader for Xamarin Forms. 

## See RealXaml in action!
[![RealXaml Video](http://www.admaiorastudio.com/wp-content/uploads/2019/04/realxaml.video_.thumb_.png)](https://youtu.be/jJO3xjcD8lQ)

## Do I need it?
No if you don't like stuff like Hot Reload and Live Reload :)

## How it helps me?
It speeds up the development of Xamarin.Forms applications letting you to edit .xaml files and see changes in real time. It also allow you to deploy the core assembly of your app to see instant changes without a full compile in all devices and simulator.

## What do you need to use it?
- [Visual Studio 2017 Extension](https://marketplace.visualstudio.com/items?itemName=AdMaioraStudio.RealXaml)
- [AdMaiora.RealXaml.Client nuget package](https://www.nuget.org/packages/AdMaiora.RealXaml.Client/)

## Getting started
To start use RealXaml in your solution you have to:

First install the RealXaml Visual Studio 2017 extension. (2019 version will come very shortly)
https://marketplace.visualstudio.com/items?itemName=AdMaioraStudio.RealXaml

  
Once the extension is installed, open or create a new Xamarin.Forms app solution. When the solution is fully loaded, you have to activate RealXaml.
To activate RealXaml just go to the Tools menu in Visual Studio and click on the "Enable RealXaml" menu item.

![alt text](http://www.admaiorastudio.com/wp-content/uploads/2019/04/realxaml.activate.png)

If the activation is successfully done a message will appear on the screen. 
If activaion fails, please see troubleshoot section down here :)

![alt text](http://www.admaiorastudio.com/wp-content/uploads/2019/04/realxaml.everything.ok_.png)

Then you need to install the AdMaiora.RealXaml.Client nuget package in your Xamarin.Forms app solution.
https://www.nuget.org/packages/AdMaiora.RealXaml.Client/

![alt text](http://www.admaiorastudio.com/wp-content/uploads/2019/04/realxaml.nugetinstall.png)

Once the nuget package is installed, using RealXaml is very easy! Just follow these steps.

First you have to modify a little your `App.xaml.cs` class. Something like this should be fine.
```c#
        public App()
        {
            AdMaiora.RealXaml.Client.AppManager.Init(this);
            InitializeComponent();

            MainPage = new MainPage();
        }
```

Then go to the `MainPage.xaml.cs' and do the same thing!
```c#
        public MainPage()
        {
            AdMaiora.RealXaml.Client.AppManager.Init(this);
            InitializeComponent();
        }
```

One more thing to do, just another simple change to your `MainPage.xaml.cs'. You have to add an attribute to tell RealXaml what page should be used as main page during hot reloading!
```c#
    [AdMaiora.RealXaml.Client.MainPage]
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class MainPage : ContentPage
    {
```

Note that we are building a single page application. Remember your `App.xaml.cs` constructor? If you want to take advange of navigation between pages in Xamarin.Forms you have to change your main page initialization like this. 
```c#
        public App()
        {
            AdMaiora.RealXaml.Client.AppManager.Init(this);
            InitializeComponent();

            MainPage = new NavigationPage(new MainPage());
        }
```

When using a `NavigationPage` as `MainPage` you should also change the attribute used to mark the MainPage for RealXaml. 
```c#
    [AdMaiora.RealXaml.Client.RootPage]
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class MainPage : ContentPage
    {
```

# You're done!

Try now changing the Xaml on your `MainPage.xaml' file and press the save button! You should see changes in real time.
This could be done having your app in debug or not, in simulator or real device!

# There's more!

Try stop the debug session and run manually your application in your simulator or device. 
Then go to the `MainPage.xaml' and a new button like this.
```xaml
<?xml version="1.0" encoding="utf-8" ?>
<ContentPage 
    xmlns="http://xamarin.com/schemas/2014/forms"
    xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
    xmlns:local="clr-namespace:RealXamlTest2"
    x:Class="RealXamlTest2.MainPage"
    BackgroundColor="#FFFFAA">

    <StackLayout>
        <!-- Place new controls here -->
        <Label Text="Welcome to Xamarin.Forms!" 
           HorizontalOptions="Center"
           VerticalOptions="CenterAndExpand" />
		   
		   
		<!-- Add this button now -->
        <Button Text="Hello RealXaml" Clicked="HelloButton_Clicked"/>
    </StackLayout>

</ContentPage>
```

Then go to the `MainPage.xaml.cs' class and add the event handler!
```c#
        private async void HelloButton_Clicked(object sender, EventArgs e)
        {
            await DisplayAlert("Hello World", "This is the first version of RealXaml", "Great");
        }
```

Now select the core project you have and BUILD! Try and see :)

![alt text](http://www.admaiorastudio.com/wp-content/uploads/2019/04/realxaml.build_.png)

## Troubleshooting

### RealXaml enable is not working
If enabling of RealXaml gives you error you should check:

- You are running Visual Studio 2017 as Administrator

- You have the latest .NET Core runtime installed
https://dotnet.microsoft.com/download

### iOS is not working
RealXaml uses SignalR behind the scene. There's a known bug in Xamarin.iOS which prevent SignalR to work. To fix this issue just read this.
https://github.com/mono/mono/issues/11731

Basically you have to remove a file from your Xamarin.iOS installation.
Move the facade assembly away from its install location:

C:\Program Files (x86)\Microsoft Visual Studio\2017\Enterprise\Common7\IDE\ReferenceAssemblies\Microsoft\Framework\Xamarin.iOS\v1.0\Facades\System.Threading.Tasks.Extensions.dll