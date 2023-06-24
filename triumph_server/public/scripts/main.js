const buttons = document.querySelectorAll(".catalog__category-button");
const nodeRoot = document.querySelector("#product-list");

async function getCatalog(categoryNameEscaped) {
    try {
        const response = await fetch(`catalog?category=${categoryNameEscaped}`);
        return [response.status, response.status === 200 ? await response.json() : null];
    } catch {
        return null;
    }
}

function enableButton(nodeButton) {
    nodeButton.classList.add("enabled");
}

function disableAllButtons() {
    buttons?.forEach(element => element.classList.remove("enabled"));
}

async function loadCategory(categoryName) {
    disableAllButtons();
    clearChildNodes(nodeRoot);
    const response = await getCatalog(encodeURIComponent(categoryName));

    if (!response) {
        nodeRoot.textContent = "База данных не найдена!";
        nodeRoot.style.marginTop = "50px";
        return;
    }

    const errorCode = response[0];
    if (errorCode === 200) {
        const categoryList = response[1];
        if (categoryList.length > 0) {
            nodeRoot.style.marginTop = "150px";
            categoryList.forEach(element => {
                const nodeImage = document.createElement("img");
                nodeImage.classList.add("catalog__product-item");
                const imageUrl = element;
                nodeImage.setAttribute("src", imageUrl);
                nodeImage.setAttribute("alt", element);

                const nodeImageContaner = document.createElement("div");
                nodeImageContaner.classList.add("product-preview-container");
                nodeImageContaner.style.position = "relative";

                const nodeImageDecor1 = document.createElement("img");
                nodeImageDecor1.setAttribute("src", "../images/decor_tr.png");
                nodeImageDecor1.style.position = "absolute";
                nodeImageDecor1.style.top = "-30px";
                nodeImageDecor1.style.right = "-30px";

                const nodeImageDecor2 = document.createElement("img");
                nodeImageDecor2.setAttribute("src", "../images/decor_bl.png");
                nodeImageDecor2.style.position = "absolute";
                nodeImageDecor2.style.bottom = "-10px";
                nodeImageDecor2.style.left = "-30px";

                nodeImageContaner.appendChild(nodeImage);
                nodeImageContaner.appendChild(nodeImageDecor1);
                nodeImageContaner.appendChild(nodeImageDecor2);
                nodeRoot.appendChild(nodeImageContaner);
            });
        } else {
            nodeRoot.textContent = "Товары не найдены!";
            nodeRoot.style.marginTop = "50px";
        }
    } else {
        nodeRoot.textContent = `Ошибка ${errorCode}!`;
        nodeRoot.style.marginTop = "50px";
    }
}

function clearChildNodes(node) {
    while (node.firstChild) node.removeChild(node.firstChild);
}

if (buttons && buttons.length > 0) {
    buttons.forEach(element => element.addEventListener("click", (e) => {
        loadCategory(e.target.name);
        enableButton(e.target);
    }));

    buttons[0].click();
} else {
    nodeRoot.textContent = "Ошибка!";
    nodeRoot.style.marginTop = "50px";
}
