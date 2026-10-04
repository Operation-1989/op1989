using UnityEngine;
using UnityEngine.InputSystem;

public class ControlSwitch : MonoBehaviour {

	public Key toggleKey = Key.C;
	public GameObject manualController;
	public GameObject automaticController;

	private bool useAutomaticControl = false;
	private const string automaticControlDefaultCMDLineArgument = "-automaticControl";

	void Awake()
	{
		Application.targetFrameRate = 60;
		useAutomaticControl = HasCommandLineArgument (automaticControlDefaultCMDLineArgument);
		SetControllerState (true);
	}

	void Update()
	{
		if (Keyboard.current != null && Keyboard.current[toggleKey].wasPressedThisFrame) {
			Toggle ();
		}
	}

	void Toggle()
	{
		useAutomaticControl = !useAutomaticControl;
		SetControllerState(useAutomaticControl);
	}

	void SetControllerState(bool useAutomaticControl)
	{
		manualController.SetActive (!useAutomaticControl);
		automaticController.SetActive (useAutomaticControl);
	}

	bool HasCommandLineArgument(string argument)
	{
		string[] passedArguments = System.Environment.GetCommandLineArgs ();
		foreach (string passedArgument in passedArguments) {
			if (passedArgument.Equals(argument))
				return true;
		}
		return false;
	}
}
