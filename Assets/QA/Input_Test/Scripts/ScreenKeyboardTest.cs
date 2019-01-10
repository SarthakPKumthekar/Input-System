using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Experimental.Input;

#if UNITY_ANDROID
using UnityEngine.Experimental.Input.Plugins.Android;
#elif UNITY_WSA
using UnityEngine.Experimental.Input.Plugins.WSA;
#endif


public enum AutomaticOperation
{
    FakeCharacterLimit
}

public class ScreenKeyboardTest : MonoBehaviour
{
    public Dropdown m_KeyboardTypeDropDown;
    public Toggle m_KeyboardAutocorrection;
    public Toggle m_KeyboardMultiline;
    public Toggle m_KeyboardSecure;
    public Toggle m_KeyboardAlert;
    public InputField m_InputField;
    public InputField m_OccludingAreaField;
    public InputField m_KeyboardStatus;
    public InputField m_KeyboardInputField;

    public InputField m_OldOccludingAreaField;
    public InputField m_OldKeyboardStatus;
    public InputField m_OldKeyboardInputField;

   // public Dropdown m_KeyboardTypeDropDown;

    public GameObject m_Info;
    public GameObject m_Log;

    public Text m_LogText;

    ScreenKeyboard m_ScreenKeyboard;
    // Start is called before the first frame update

    TouchScreenKeyboard m_OldScreenKeyboard;

    void Start()
    {
        m_ScreenKeyboard = ScreenKeyboard.GetInstance();
        m_KeyboardTypeDropDown.ClearOptions();

        m_ScreenKeyboard.statusChanged += StatusChangedCallback;
        m_ScreenKeyboard.inputFieldTextChanged += InputFieldTextChanged;



        foreach (var t in Enum.GetValues(typeof(ScreenKeyboardType)))
        {
            m_KeyboardTypeDropDown.options.Add(new Dropdown.OptionData(t.ToString()));
        }
        m_KeyboardTypeDropDown.RefreshShownValue();

        m_LogText.text = "";


    }

    private void InputFieldTextChanged(InputFieldEventArgs args)
    {
        m_LogText.text += string.Format("Input: {0} ({1}, {2})", args.text, args.selection.start, args.selection.length) + Environment.NewLine;
        m_InputField.text = args.text;
    }

    private void StatusChangedCallback(ScreenKeyboardStatus status)
    {
        m_LogText.text += "Status: " + status + Environment.NewLine;
    }

    // Update is called once per frame
    void Update()
    {
        m_OccludingAreaField.text = m_ScreenKeyboard.occludingArea.ToString();
        m_KeyboardStatus.text = m_ScreenKeyboard.status.ToString();
        m_KeyboardInputField.text = m_ScreenKeyboard.inputFieldText;

        if (m_OldScreenKeyboard != null)
        {
            m_OldOccludingAreaField.text = TouchScreenKeyboard.area.ToString();
            m_OldKeyboardStatus.text = m_OldScreenKeyboard.status.ToString();
            m_OldKeyboardInputField.text = m_OldScreenKeyboard.text;
        }
    }

    private ScreenKeyboardType ToScreenKeyboardType(string value)
    {
        return (ScreenKeyboardType)Enum.Parse(typeof(ScreenKeyboardType), value);
    }

    public void Show()
    {
        ScreenKeyboardShowParams showParams = new ScreenKeyboardShowParams()
        {
            initialText = m_InputField.text,
            autocorrection = m_KeyboardAutocorrection.isOn,
            multiline = m_KeyboardMultiline.isOn,
            secure = m_KeyboardSecure.isOn,
            alert = m_KeyboardAlert.isOn,
            type = ToScreenKeyboardType(m_KeyboardTypeDropDown.captionText.text)

        };

        m_ScreenKeyboard.Show(showParams);
    }

    private TouchScreenKeyboardType ToTouchScreenKeyboardType(string value)
    {
        return (TouchScreenKeyboardType)Enum.Parse(typeof(TouchScreenKeyboardType), value);
    }

    public void ShowOldKeyboard()
    {
        m_OldScreenKeyboard = TouchScreenKeyboard.Open(m_InputField.text,
            ToTouchScreenKeyboardType(m_KeyboardTypeDropDown.captionText.text),
            m_KeyboardAutocorrection.isOn,
            m_KeyboardMultiline.isOn,
            m_KeyboardSecure.isOn,
            m_KeyboardAlert.isOn,
            "No placeholder");
    }

    public void ShowInfo()
    {
        m_Info.SetActive(true);
        m_Log.SetActive(false);
    }

    public void ShowLog()
    {
        m_Info.SetActive(false);
        m_Log.SetActive(true);
    }

    public void Hide()
    {
        m_ScreenKeyboard.Hide();
    }
}
