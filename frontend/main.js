window.addEventListener('DOMContentLoaded', (event) =>{
    getVisitCount();
})

const functionApiUrl = 'https://getazureresumecounterew.azurewebsites.net/api/GetResumeCounter?code=mSHux-ZeS5R4DENy0-Y30y2dzk881G6w9M0qLDwLnqfIAzFukSof2A==';
const LocalFunctionApi = 'http://localhost:7071/api/GetResumeCounter';

const getVisitCount = () => {
    let count = 30;
    fetch(functionApiUrl).then(response => {
        return response.json()

    }).then(response => {
        console.log("Website called function API.");
        count = response.count;
        document.getElementById("counter").innerText = count;
    }).catch(function(error){
        console.log(error);
    });
    return count;
} 